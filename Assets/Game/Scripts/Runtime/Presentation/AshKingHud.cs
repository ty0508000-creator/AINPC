using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class AshKingHud : MonoBehaviour
{
    [SerializeField] AshKingArena arena;
    [SerializeField] GameObject panel;
    [SerializeField] Image healthFill;
    [SerializeField] TMP_Text title, hint;
    void Update()
    {
        var boss=arena!=null?arena.Boss:null;bool show=boss!=null&&boss.Fighting;
        if(panel!=null)panel.SetActive(show);if(!show)return;
        healthFill.fillAmount=Mathf.Clamp01(boss.Health/boss.MaxHealth);
        healthFill.rectTransform.sizeDelta=new Vector2(520*healthFill.fillAmount,12);
        title.text="재의 왕"+(boss.Enraged?" · 잿불의 광폭":"")+"   "+Mathf.CeilToInt(boss.Health)+" / "+Mathf.CeilToInt(boss.MaxHealth);
        hint.text=boss.Phase==AshKingBoss.CombatPhase.Telegraph?PatternHint(boss.Pattern):(boss.Phase==AshKingBoss.CombatPhase.Recovering?"공격 후 빈틈":boss.Phase==AshKingBoss.CombatPhase.Charging?"돌진 · 검로에서 벗어나라":"");
    }
    static string PatternHint(AshKingBoss.AttackPattern pattern)
    {
        switch(pattern)
        {
            case AshKingBoss.AttackPattern.Cleave:return "정면 베기 · 뒤나 옆으로 회피";
            case AshKingBoss.AttackPattern.Thrust:return "직선 찌르기 · 검로 옆으로 회피";
            case AshKingBoss.AttackPattern.CrossSlash:return "십자 베기 · 대각선 빈틈으로";
            case AshKingBoss.AttackPattern.Spin:return "회전 베기 · 원 밖으로";
            case AshKingBoss.AttackPattern.SwordRain:return "검우 · 고정된 낙하 원에서 벗어나라";
            case AshKingBoss.AttackPattern.Charge:return "돌진 · 직선 검로를 피하라";
            case AshKingBoss.AttackPattern.RingBurst:return "고리 폭발 · 옥색 중심이나 바깥으로";
            default:return "재 폭발 · 옥색 안전지대로 이동";
        }
    }
}
