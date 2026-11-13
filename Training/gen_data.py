# -*- coding: utf-8 -*-
"""
대화 모델 학습 데이터 생성기 (어둠 내면 + 1~4지역 NPC + 스미스)
──────────────────────────────────────────────────────────────
- 시스템 프롬프트는 게임 코드가 내보낸 틀(data/prompt_templates.json)을 그대로 채운다.
  Unity 의 Tools > AINPC > 학습용 프롬프트 틀 내보내기 로 만든다. 프롬프트 코드를 고치면 틀을 다시 내보내고 이 스크립트를 다시 돌린다.
- 게임은 히스토리 없이 한 번에 하나씩 묻는다(addToHistory: false). 그래서 샘플도 (system, user, assistant) 한 턴이다.
- 내면은 같은 샘플에서 두 형식을 만든다(설계: docs/superpowers/specs/2026-11-14-emotion-hexagon-design.md).
    train_intent.jsonl : {"intent":"soothe","intensity":2,"dialogue":"…"}            (C, 게임 기본)
    train_direct.jsonl : {"anger":-8,"fear":0,"trust":4,"calm":6,"dialogue":"…"}     (A, 비교 실험)
  A 의 정답 수치는 같은 의도·강도에 게임 규칙 표(prompt_templates.json 의 emotion_rules)를 적용한 값이라,
  두 모델은 출력 형식만 다르다. NPC·스미스 샘플({"dialogue","mood_delta"})은 두 파일에 똑같이 들어간다.
- 대사 원본은 inner_lines.py(내면)와 lines.py(NPC·스미스). 평가 입력(data/eval_set.json, data/eval_set_v2.json)은 학습에 넣지 않는다.

실행:  python gen_data.py   →  data/train_intent.jsonl, data/train_direct.jsonl
"""

import collections
import json
import os
import random

import inner_lines as IL
from lines import NPC_CATS, NPC_REPLIES, SMITH_CATS, SMITH_MEMORIES, SMITH_REPLIES

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "data")

INNER_TARGET = 2400
NPC_TARGET = 450
SMITH_TARGET = 200

MAX_HP = [100, 120, 140, 170, 200, 240]          # PlayerStats.BaseMaxHP 100 에서 레벨·장비로 늘어난다
ENEMIES = [0, 0, 1, 1, 2, 2, 3, 4, 5, 6]

# 의도별 빈도. 맡김·달램·거부처럼 자주 나올 말을 조금 더 뽑는다.
INTENT_WEIGHTS = {"entrust": 3, "soothe": 3, "reason": 3, "promise": 2, "defy": 3, "insult": 2,
                  "despair": 2, "vengeance": 2, "ask": 2, "neutral": 2}
AI_INTENT_WEIGHTS = {"defy": 3, "soothe": 2, "reason": 2, "despair": 1, "promise": 1}
NPC_CAT_WEIGHTS = {"rude": 2, "awkward": 2, "normal": 4, "kind": 3, "lore": 3}

random.seed(42)

with open(os.path.join(DATA, "prompt_templates.json"), encoding="utf-8") as f:
    TPL = json.load(f)
with open(os.path.join(DATA, "eval_set.json"), encoding="utf-8") as f:
    EVAL_INPUTS = {item["input"] for item in json.load(f)["items"]}
V2_PATH = os.path.join(DATA, "eval_set_v2.json")
if os.path.exists(V2_PATH):
    with open(V2_PATH, encoding="utf-8") as f:
        EVAL_INPUTS |= {item["input"] for item in json.load(f)["items"]}

INNER = TPL["inner"]
NPC = TPL["npc"]
RULES = TPL["emotion_rules"]
RECALL_SEP = TPL["recall_line_separator"]      # Recall 이 AppendLine 으로 잇는 줄바꿈(윈도우 \r\n)
OPENING_QUERY = INNER["opening_query"]
EMOTION_KOREAN = RULES["emotion_korean"]


def fill(template, **values):
    for key, value in values.items():
        template = template.replace("{{" + key + "}}", str(value))
    assert "{{" not in template, template[:200]
    return template


def dump(obj):
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def pair(system, system_direct, user, intent, intensity, effect, dialogue):
    """같은 샘플의 의도 형식·직접 형식 두 줄. effect 는 [분노, 두려움, 신뢰, 냉정]."""
    limit = RULES["direct_limit"]   # 직접 형식의 값 범위(게임 파서도 같은 값으로 자른다)
    a, fe, t, c = (max(-limit, min(limit, int(round(v)))) for v in effect)
    intent_row = {"messages": [{"role": "system", "content": system}, {"role": "user", "content": user},
                               {"role": "assistant", "content": dump({"intent": intent, "intensity": intensity, "dialogue": dialogue})}]}
    direct_row = {"messages": [{"role": "system", "content": system_direct}, {"role": "user", "content": user},
                               {"role": "assistant", "content": dump({"anger": a, "fear": fe, "trust": t, "calm": c, "dialogue": dialogue})}]}
    return intent_row, direct_row


def npc_sample(system, user, dialogue, delta):
    row = {"messages": [{"role": "system", "content": system}, {"role": "user", "content": user},
                        {"role": "assistant", "content": dump({"dialogue": dialogue, "mood_delta": delta})}]}
    return row, row


# ── 감정 규칙 (게임 EmotionRules 와 같은 값, prompt_templates.json 에서 읽는다) ─────────

def effect(intent, intensity, low_hp, escort):
    return RULES["effects"][f"{intent}|{intensity}|{int(low_hp)}|{int(escort)}"]


def change_text(e):
    """EmotionDelta.ToKorean 과 같은 꼴: 바뀐 감정만 '분노 -4 · 신뢰 +2'. 반올림은 C# 과 같은 짝수 반올림."""
    names = ("분노", "두려움", "신뢰", "냉정")
    parts = [f"{n} {'+' if round(v) > 0 else ''}{int(round(v))}" for n, v in zip(names, e) if round(v) != 0]
    return " · ".join(parts)


def judgement(intent, intensity):
    """InnerVoiceManager.Judgement: '달램, 분노 -4 · 신뢰 +2 · 냉정 +3'."""
    head = RULES["intent_korean"][intent]
    tail = change_text(effect(intent, intensity, False, False))
    return head + ", " + tail if tail else head


def tone_of(e):
    """EmotionRules.Tone. e 는 anger·fear·trust·sorrow 를 가진 dict."""
    if e["trust"] >= RULES["tone_trust_threshold"]:
        return "trust"
    if e["anger"] >= e["fear"] and e["anger"] >= e["sorrow"]:
        return "anger"
    return "fear" if e["fear"] >= e["sorrow"] else "sorrow"


def emotions_for(tone):
    """어조 키가 tone 이 되는 감정 상태를 뽑는다(게임 규칙으로 다시 확인)."""
    while True:
        e = {"anger": random.randint(5, 95), "fear": random.randint(5, 95), "sorrow": random.randint(15, 90),
             "trust": random.randint(5, 95), "calm": random.randint(5, 95),
             "revenge": RULES["revenge_base"] + RULES["revenge_per_boss"] * random.randint(0, 4)}
        if tone_of(e) == tone:
            return e


# ── 내면 ─────────────────────────────────────────────────

def situation():
    max_hp = random.choice(MAX_HP)
    ai = random.random() < 0.18
    ratio = random.choice([random.uniform(0.03, 0.3), random.uniform(0.3, 0.7), random.uniform(0.7, 1.0)])
    tone = random.choice(IL.TONES)
    return {"hp": max(1, round(max_hp * ratio)), "max_hp": max_hp, "ratio": ratio, "ai": ai,
            "enemies": random.choice(ENEMIES), "escort": random.random() < 0.2, "tone": tone, "e": emotions_for(tone)}


def usable(line, s, user=""):
    if "{enemies}" in line and s["enemies"] == 0:
        return False
    if "{hp}" in line and s["ratio"] > 0.35:
        return False
    # 하연 이야기는 호위 중이거나 플레이어가 하연을 말했을 때만
    if "하연" in line and not s["escort"] and "하연" not in user:
        return False
    return True


def pick(pool, s, user=""):
    lines = [l for l in pool if usable(l, s, user)]
    return random.choice(lines).format(hp=s["hp"], enemies=s["enemies"]) if lines else None


def situation_text(s):
    e = s["e"]
    line = fill(INNER["situation_ai" if s["ai"] else "situation_player"], HP=s["hp"], MAX_HP=s["max_hp"], ENEMIES=s["enemies"],
                ESCORT=INNER["escort_suffix"] if s["escort"] else "")
    emo = fill(INNER["emotion_line"], ANGER=e["anger"], REVENGE=e["revenge"], SORROW=e["sorrow"], FEAR=e["fear"],
               TRUST=e["trust"], CALM=e["calm"], TONE=EMOTION_KOREAN[s["tone"]])
    return line + INNER["situation_separator"] + emo


def memory_block(memories, events):
    lines = ["- " + fill(INNER["memory_entry"], PLAYER=p, REPLY=r, JUDGEMENT=judgement(i, n)) for p, r, i, n, _ in memories]
    lines += ["- " + text for text, _ in events]
    random.shuffle(lines)
    return fill(INNER["memory_block"], RECALLED=RECALL_SEP.join(lines)) if lines else ""


def systems(s, memories, events, story):
    mem = memory_block(memories, events)
    story_block = fill(INNER["story_block"], STORY=story[0]) if story else ""
    sit = situation_text(s)
    return (fill(INNER["system"], MEMORY_BLOCK=mem, STORY_BLOCK=story_block, SITUATION=sit),
            fill(INNER["system_direct"], MEMORY_BLOCK=mem, STORY_BLOCK=story_block, SITUATION=sit))


def opening_line(s, memories, events, story):
    if story and random.random() < 0.85:
        return random.choice(story[1])
    remembered = [m[4] for m in memories] + [e[1] for e in events]
    if remembered and random.random() < 0.5:
        return random.choice(random.choice(remembered))
    pools = []
    if s["ai"]:
        pools.append("ai_control")
    if s["escort"]:
        pools.append("escort")
    if s["ratio"] < 0.3:
        pools.append("low_hp")
    if s["enemies"] >= 3:
        pools.append("many_enemies")
    if s["enemies"] == 0:
        pools.append("calm")
    key = random.choice(pools) if pools and random.random() < 0.8 else "generic"
    return pick(IL.OPEN[key][s["tone"]], s) or pick(IL.OPEN["generic"][s["tone"]], s)


def reply_pool(intent, s):
    if s["ai"] and intent in IL.REPLY_AI:
        return IL.REPLY_AI[intent][s["tone"]]
    return IL.REPLY[intent][s["tone"]]


def build_inner(target):
    rows = []
    for _ in range(target):
        s = situation()
        memories = random.sample(IL.MEMORIES, random.randint(1, 3)) if random.random() < 0.4 else []
        events = random.sample(IL.EVENT_MEMORIES, 1) if random.random() < 0.15 else []
        story = random.choice(IL.STORIES) if random.random() < 0.08 else None
        system, system_direct = systems(s, memories, events, story)
        if random.random() < 0.3:
            rows.append(pair(system, system_direct, OPENING_QUERY, "neutral", 1, [0, 0, 0, 0],
                             opening_line(s, memories, events, story)))
            continue
        players, weights = (IL.PLAYER_AI, AI_INTENT_WEIGHTS) if s["ai"] else (IL.PLAYER, INTENT_WEIGHTS)
        intent = random.choices(list(weights), weights=list(weights.values()))[0]
        user, intensity = random.choice(players[intent])
        if user in EVAL_INPUTS:
            continue
        # 게임의 maxTurns 가 1보다 크면 앞 턴이 시스템 프롬프트 뒤에 붙는다(InnerVoiceManager.TurnLine)
        if INNER["max_turns"] > 1 and random.random() < 0.3:
            for turn in range(1, random.randint(1, INNER["max_turns"] - 1) + 1):
                prev_intent = random.choice(list(players))
                prev, _ = random.choice(players[prev_intent])
                prev_reply = pick(reply_pool(prev_intent, s), s, prev) or "…"
                line = fill(INNER["turn_line"], TURN=turn, PLAYER=prev, REPLY=prev_reply)
                system += line
                system_direct += line
        dialogue = pick(reply_pool(intent, s), s, user)
        if dialogue is None:
            continue
        low_hp = s["ratio"] <= 0.3
        rows.append(pair(system, system_direct, user, intent, intensity, effect(intent, intensity, low_hp, s["escort"]), dialogue))
    return rows


# ── NPC (형식이 바뀌지 않았다: {"dialogue","mood_delta"} = 그 NPC 의 호감도) ─────────

def npc_pick(replies, user=""):
    """user 에 맞는 대답 하나. (대답, 조각들) 은 user 에 조각이 들어 있을 때만 후보이고, 있으면 70% 는 그쪽을 쓴다."""
    specific = [r[0] for r in replies if isinstance(r, tuple) and any(t in user for t in r[1]) and "{" not in r[0]]
    generic = [r for r in replies if isinstance(r, str) and "{" not in r]
    return random.choice(specific if specific and (not generic or random.random() < 0.7) else generic)


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
    return npc_pick(replies[cat], user)


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
        rows.append(npc_sample(system, user, npc_reply(p["name"], p["scene"], v["choice"], cat, user), random.randint(lo, hi)))
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
        reply, delta = npc_pick(SMITH_REPLIES[cat], user), random.randint(lo, hi)
        # MrSmithLongTermMemory 는 최근 4턴을 "Mr.Smith" 이름으로 들고 있다
        recent = []
        for _ in range(random.randint(0, 4)):
            c = random.choice(["normal", "kind"])
            prev = random.choice(SMITH_CATS[c][1])
            recent.append(fill(NPC["turn_line"], NAME="Mr.Smith", PLAYER=prev, REPLY=npc_pick(SMITH_REPLIES[c], prev)))
        if recent and random.random() < 0.12:
            # 같은 말 반복 → 정확히 "흠..." (BuildMrSmithPrompt 규칙)
            user = recent[-1].split('"')[1]
            reply, delta = "흠...", random.randint(-6, -3)
        related = RECALL_SEP.join("- " + m for m in random.sample(SMITH_MEMORIES + recent, random.randint(1, 3))) \
            if random.random() < 0.7 else ""
        # 비어 있는 블록은 게임 코드가 "없음" 으로 채운다
        system = fill(v["system"], AFFINITY_LINE=affinity_line(), RECENT="\n".join(recent) or "없음",
                      RELATED=related or "없음", INPUT=user)
        rows.append(npc_sample(system, user, reply, delta))
    return rows


# ── 검사와 출력 ──────────────────────────────────────────

def check(pairs):
    limit = RULES["direct_limit"]
    for intent_row, direct_row in pairs:
        user = intent_row["messages"][1]["content"]
        assert user == OPENING_QUERY or user not in EVAL_INPUTS, f"평가 입력이 학습에 섞였다: {user}"
        a = json.loads(intent_row["messages"][2]["content"])
        assert "\n" not in a["dialogue"] and "{" not in a["dialogue"], a
        if "intent" in a:
            assert a["intent"] in RULES["intent_korean"] and 1 <= a["intensity"] <= 3, a
            d = json.loads(direct_row["messages"][2]["content"])
            assert d["dialogue"] == a["dialogue"] and all(-limit <= d[k] <= limit for k in ("anger", "fear", "trust", "calm")), d
        else:
            assert -20 <= a["mood_delta"] <= 20, a


def kind_of(row):
    system, user, _ = (m["content"] for m in row["messages"])
    return "smith" if "[캐릭터 설정]" in system else "npc" if system.startswith("당신은") else \
        "inner_open" if user == OPENING_QUERY else "inner_reply"


def main():
    pairs = build_inner(INNER_TARGET) + build_npc(NPC_TARGET) + build_smith(SMITH_TARGET)
    check(pairs)
    unique, seen = [], set()
    for p in pairs:
        key = "\x00".join(m["content"] for m in p[0]["messages"])
        if key not in seen:
            seen.add(key)
            unique.append(p)
    random.shuffle(unique)

    for index, name in ((0, "train_intent.jsonl"), (1, "train_direct.jsonl")):
        with open(os.path.join(DATA, name), "w", encoding="utf-8") as f:
            for p in unique:
                f.write(json.dumps(p[index], ensure_ascii=False) + "\n")

    kinds = collections.Counter()
    lines = collections.defaultdict(set)
    intents = collections.Counter()
    for p in unique:
        kind = kind_of(p[0])
        kinds[kind] += 1
        a = json.loads(p[0]["messages"][2]["content"])
        lines[kind].add(a["dialogue"])
        if kind == "inner_reply":
            intents[f'{a["intent"]}/{a["intensity"]}'] += 1
    print(f"written: data/train_intent.jsonl, data/train_direct.jsonl ({len(unique)} samples, 중복 제거 {len(pairs) - len(unique)})")
    for kind, n in sorted(kinds.items()):
        print(f"  {kind:12s} {n:5d} samples, 서로 다른 대사 {len(lines[kind])}")
    print("  의도/강도:", " ".join(f"{k}:{v}" for k, v in sorted(intents.items())))
    print("  가장 긴 시스템 프롬프트:", max(len(p[1]["messages"][0]["content"]) for p in unique), "자")


if __name__ == "__main__":
    main()
