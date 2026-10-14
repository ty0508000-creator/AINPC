# -*- coding: utf-8 -*-
"""
대화 모델 학습 데이터 생성기 (어둠 내면 + 1~4지역 NPC + 스미스)
──────────────────────────────────────────────────────────────
- 시스템 프롬프트는 게임 코드가 내보낸 틀(data/prompt_templates.json)을 그대로 채운다.
  Unity 의 Tools > AINPC > 학습용 프롬프트 틀 내보내기 로 만든다. 프롬프트 코드를 고치면 틀을 다시 내보내고 이 스크립트를 다시 돌린다.
- 게임은 히스토리 없이 한 번에 하나씩 묻는다(addToHistory: false). 그래서 샘플도 (system, user, assistant) 한 턴이다.
  내면의 첫 마디는 "대화를 시작해." 에 대한 대답이고, 플레이어의 대답 요청에는 첫 마디가 들어가지 않는다.
- 대사 원본은 lines.py, 평가 입력(data/eval_set.json)은 학습에 넣지 않는다.
- assistant 응답은 게임 grammar 와 같은 꼴 {"dialogue":"…","mood_delta":정수} (공백 없음).

실행:  python gen_data.py   →  data/train_data.jsonl
"""

import collections
import json
import os
import random

from lines import (MEMORIES, NPC_CATS, NPC_REPLIES, OPEN, REPLY, REPLY_AI, SMITH_CATS, SMITH_MEMORIES,
                   SMITH_REPLIES, STORIES)

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "data")

INNER_TARGET = 1500
NPC_TARGET = 450
SMITH_TARGET = 200

MAX_HP = [100, 120, 140, 170, 200, 240]          # PlayerStats.BaseMaxHP 100 에서 레벨·장비로 늘어난다
ENEMIES = [0, 0, 1, 1, 2, 2, 3, 4, 5, 6]

PLAYER_CAT_WEIGHTS = {"submit": 3, "defiant": 3, "insult": 2, "soothe": 3, "hesitant_yes": 2, "bargain": 2,
                      "bargain_reject": 1, "fear": 2, "question": 2, "ignore": 2, "threaten_back": 2,
                      "hesitant": 2, "ally": 2}
AI_CAT_WEIGHTS = {"plead_nice": 3, "demand": 3, "calm_persuade": 2, "panic": 2}
NPC_CAT_WEIGHTS = {"rude": 2, "awkward": 2, "normal": 4, "kind": 3, "lore": 3}

random.seed(42)

with open(os.path.join(DATA, "prompt_templates.json"), encoding="utf-8") as f:
    TPL = json.load(f)
with open(os.path.join(DATA, "eval_set.json"), encoding="utf-8") as f:
    EVAL_INPUTS = {item["input"] for item in json.load(f)["items"]}

INNER = TPL["inner"]
NPC = TPL["npc"]
RECALL_SEP = TPL["recall_line_separator"]      # Recall 이 AppendLine 으로 잇는 줄바꿈(윈도우 \r\n)
OPENING_QUERY = INNER["opening_query"]


def fill(template, **values):
    for key, value in values.items():
        template = template.replace("{{" + key + "}}", str(value))
    assert "{{" not in template, template[:200]
    return template


def answer(dialogue, delta):
    return json.dumps({"dialogue": dialogue, "mood_delta": delta}, ensure_ascii=False, separators=(",", ":"))


def sample(system, user, dialogue, delta):
    return {"messages": [{"role": "system", "content": system},
                         {"role": "user", "content": user},
                         {"role": "assistant", "content": answer(dialogue, delta)}]}


# ── 내면 ─────────────────────────────────────────────────

def situation():
    max_hp = random.choice(MAX_HP)
    ai = random.random() < 0.18
    ratio = random.choice([random.uniform(0.03, 0.3), random.uniform(0.3, 0.7), random.uniform(0.7, 1.0)])
    return {"hp": max(1, round(max_hp * ratio)), "max_hp": max_hp, "ratio": ratio,
            # 어둠이 몸을 쥐는 건 감정 수치가 바닥일 때다
            "mood": random.randint(3, 30) if ai else random.randint(5, 95),
            "enemies": random.choice(ENEMIES), "ai": ai}


def usable(line, s):
    if s is None:
        return "{" not in line
    if "{enemies}" in line and s["enemies"] == 0:
        return False
    if "{hp}" in line and s["ratio"] > 0.35:
        return False
    return True


def pick(replies, user="", s=None):
    """user 에 맞는 대답 하나. (대답, 조각들) 은 user 에 조각이 들어 있을 때만 후보이고, 있으면 70% 는 그쪽을 쓴다."""
    specific = [r[0] for r in replies if isinstance(r, tuple) and any(t in user for t in r[1]) and usable(r[0], s)]
    generic = [r for r in replies if isinstance(r, str) and usable(r, s)]
    line = random.choice(specific if specific and (not generic or random.random() < 0.7) else generic)
    return line.format(hp=s["hp"], enemies=s["enemies"]) if s else line


def inner_system(s, memories, story):
    recalled = RECALL_SEP.join("- " + fill(INNER["memory_entry"], PLAYER=p, REPLY=r, SIGNED_DELTA=f"{d:+d}")
                               for p, r, d, _ in memories)
    memory_block = fill(INNER["memory_block"], RECALLED=recalled) if memories else ""
    story_block = fill(INNER["story_block"], STORY=story[0]) if story else ""
    situation_line = fill(INNER["situation_ai" if s["ai"] else "situation_player"],
                          HP=s["hp"], MAX_HP=s["max_hp"], MOOD=s["mood"], ENEMIES=s["enemies"])
    return fill(INNER["system"], MEMORY_BLOCK=memory_block, STORY_BLOCK=story_block, SITUATION=situation_line)


def opening_line(s, memories, story):
    if story and random.random() < 0.85:
        return random.choice(story[1])
    if memories and random.random() < 0.5:
        return random.choice(random.choice(memories)[3])
    pools = []
    if s["ai"]:
        pools.append("ai_control")
    if s["ratio"] < 0.3:
        pools.append("low_hp")
    if s["enemies"] >= 3:
        pools.append("many_enemies")
    if s["enemies"] == 0:
        pools.append("calm")
    if s["mood"] <= 30:
        pools.append("low_mood")
    if s["mood"] >= 70:
        pools.append("high_mood")
    pool = OPEN[random.choice(pools)] if pools and random.random() < 0.75 else OPEN["generic"]
    return pick(pool, s=s)


def build_inner(target):
    rows = []
    for _ in range(target):
        s = situation()
        memories = random.sample(MEMORIES, random.randint(1, 3)) if random.random() < 0.4 else []
        story = random.choice(STORIES) if random.random() < 0.08 else None
        system = inner_system(s, memories, story)
        if random.random() < 0.35:
            rows.append(sample(system, OPENING_QUERY, opening_line(s, memories, story), 0))
            continue
        cats, weights = (REPLY_AI, AI_CAT_WEIGHTS) if s["ai"] else (REPLY, PLAYER_CAT_WEIGHTS)
        cat = random.choices(list(weights), weights=list(weights.values()))[0]
        players, replies, (lo, hi) = cats[cat]
        # 게임의 maxTurns 가 1보다 크면 앞 턴이 시스템 프롬프트 뒤에 붙는다(InnerVoiceManager.TurnLine)
        if INNER["max_turns"] > 1 and random.random() < 0.3:
            prev_players, prev_replies, _ = cats[random.choice(list(cats))]
            for turn in range(1, random.randint(1, INNER["max_turns"] - 1) + 1):
                prev = random.choice(prev_players)
                system += fill(INNER["turn_line"], TURN=turn, PLAYER=prev, REPLY=pick(prev_replies, prev, s))
        user = random.choice(players)
        rows.append(sample(system, user, pick(replies, user, s), random.randint(lo, hi)))
    return rows


# ── NPC ─────────────────────────────────────────────────

def affinity_line():
    return fill(NPC["affinity_line"], AFFINITY=random.randint(5, 95))


def npc_reply(name, scene, choice, cat, user):
    replies = NPC_REPLIES[name]
    region = f"place_r{scene[-1]}"
    # 2~4지역에서 "여기는 어떤 곳" · "왜 말하지 않죠" 에는 그 지역 이야기를 섞는다
    if region in replies and any(t in user for t in ("어떤 곳", "말하지 않죠")) and random.random() < 0.6:
        return random.choice(replies[region])
    # 1지역의 소윤 선택 상태는 인사와 호의에 대한 대답에 드러난다
    greeting = cat == "normal" and any(t in user for t in ("안녕", "지냈어요", "지내요"))
    if choice != "none" and (cat == "kind" or greeting) and random.random() < 0.5:
        return random.choice(replies[choice])
    return pick(replies[cat], user)


def build_npc(target):
    personas = [p for p in NPC["personas"] if not p["smith"]]
    rows = []
    for _ in range(target):
        p = random.choice(personas)
        v = random.choices(p["variants"], weights=[2] + [1] * (len(p["variants"]) - 1))[0]
        cat = random.choices(list(NPC_CAT_WEIGHTS), weights=list(NPC_CAT_WEIGHTS.values()))[0]
        (lo, hi), players = NPC_CATS[cat]
        recent = []
        if random.random() < 0.5:
            for _ in range(random.randint(1, 4)):   # NPCInteraction 은 최근 4턴까지 들고 있다
                c = random.choice(["normal", "kind", "lore"])
                prev = random.choice(NPC_CATS[c][1])
                recent.append(fill(NPC["turn_line"], NAME=p["name"], PLAYER=prev,
                                   REPLY=npc_reply(p["name"], p["scene"], v["choice"], c, prev)))
        system = (fill(v["system_with_recent"], AFFINITY_LINE=affinity_line(), RECENT="\n".join(recent)) if recent
                  else fill(v["system_no_recent"], AFFINITY_LINE=affinity_line()))
        user = random.choice(players)
        rows.append(sample(system, user, npc_reply(p["name"], p["scene"], v["choice"], cat, user), random.randint(lo, hi)))
    return rows


def build_smith(target):
    personas = [p for p in NPC["personas"] if p["smith"]]
    rows = []
    for _ in range(target):
        p = random.choice(personas)
        v = random.choice(p["variants"])
        cat = random.choices(list(NPC_CAT_WEIGHTS), weights=[2, 2, 4, 3, 0])[0]   # 스미스엔 지난 일 질문 범주가 없다
        (lo, hi), players = SMITH_CATS[cat]
        user = random.choice(players)
        reply, delta = pick(SMITH_REPLIES[cat], user), random.randint(lo, hi)
        # MrSmithLongTermMemory 는 최근 4턴을 "Mr.Smith" 이름으로 들고 있다
        recent = []
        for _ in range(random.randint(0, 4)):
            c = random.choice(["normal", "kind"])
            prev = random.choice(SMITH_CATS[c][1])
            recent.append(fill(NPC["turn_line"], NAME="Mr.Smith", PLAYER=prev, REPLY=pick(SMITH_REPLIES[c], prev)))
        if recent and random.random() < 0.12:
            # 같은 말 반복 → 정확히 "흠..." (BuildMrSmithPrompt 규칙)
            user = recent[-1].split('"')[1]
            reply, delta = "흠...", random.randint(-6, -3)
        related = RECALL_SEP.join("- " + m for m in random.sample(SMITH_MEMORIES + recent, random.randint(1, 3))) \
            if random.random() < 0.7 else ""
        # 비어 있는 블록은 게임 코드가 "없음" 으로 채운다
        system = fill(v["system"], AFFINITY_LINE=affinity_line(), RECENT="\n".join(recent) or "없음",
                      RELATED=related or "없음", INPUT=user)
        rows.append(sample(system, user, reply, delta))
    return rows


# ── 검사와 출력 ──────────────────────────────────────────

def check(rows):
    for r in rows:
        user = r["messages"][1]["content"]
        assert user == OPENING_QUERY or user not in EVAL_INPUTS, f"평가 입력이 학습에 섞였다: {user}"
        a = json.loads(r["messages"][2]["content"])
        assert -20 <= a["mood_delta"] <= 20 and "\n" not in a["dialogue"] and "{" not in a["dialogue"], a


def main():
    rows = build_inner(INNER_TARGET) + build_npc(NPC_TARGET) + build_smith(SMITH_TARGET)
    check(rows)
    unique, seen = [], set()
    for r in rows:
        key = "\x00".join(m["content"] for m in r["messages"])
        if key not in seen:
            seen.add(key)
            unique.append(r)
    random.shuffle(unique)

    out_path = os.path.join(DATA, "train_data.jsonl")
    with open(out_path, "w", encoding="utf-8") as f:
        for r in unique:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")

    kinds = collections.Counter()
    lines = collections.defaultdict(set)
    deltas = collections.Counter()
    for r in unique:
        system, user, a = (m["content"] for m in r["messages"])
        kind = "smith" if "[캐릭터 설정]" in system else "npc" if system.startswith("당신은") else \
            "inner_open" if user == OPENING_QUERY else "inner_reply"
        kinds[kind] += 1
        a = json.loads(a)
        lines[kind].add(a["dialogue"])
        deltas[a["mood_delta"]] += 1
    print(f"written: {out_path} ({len(unique)} samples, 중복 제거 {len(rows) - len(unique)})")
    for kind, n in sorted(kinds.items()):
        print(f"  {kind:12s} {n:5d} samples, 서로 다른 대사 {len(lines[kind])}")
    print("  mood_delta:", " ".join(f"{d}:{deltas[d]}" for d in range(-20, 21) if deltas[d]))
    print("  가장 긴 시스템 프롬프트:", max(len(r["messages"][0]["content"]) for r in unique), "자")


if __name__ == "__main__":
    main()
