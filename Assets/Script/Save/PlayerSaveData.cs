using System.Collections.Generic;

[System.Serializable]
public class PlayerSaveData
{
    public int level;
    public float hp;
    public float maxHP;
    public float mana;
    public float maxMana;
    public float exp;
    public float maxEXP;

    // ── 진행 ──
    // 기존 필드는 그대로 두고 아래를 덧붙였다. 옛 세이브 파일을 읽어도
    // 없는 값은 기본값(빈 문자열 / 0 / false)으로 들어와 그냥 동작한다.

    /// <summary>마지막으로 있던 씬.</summary>
    public string sceneName;

    /// <summary>그 씬에서의 위치.</summary>
    public float posX;
    public float posY;

    /// <summary>보스 처치 같은 진행 표시.</summary>
    public List<string> flags = new List<string>();

    /// <summary>씬/위치가 채워져 있어 이어하기가 가능한가.</summary>
    public bool hasProgress;
}
