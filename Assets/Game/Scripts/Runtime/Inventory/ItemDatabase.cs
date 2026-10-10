using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임에 있는 모든 아이템 정의 목록. 저장 파일에는 itemId 만 남으므로
/// UI 가 아이콘·설명을 찾을 때 이 목록에서 itemId 로 찾는다.
/// <c>Tools > AINPC > Inventory > Build Inventory UI</c> 를 돌리면 프로젝트의 ItemDefinition 으로 다시 채워진다.
/// </summary>
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "AINPC/Item Database")]
public sealed class ItemDatabase : ScriptableObject
{
    static ItemDatabase instance;

    /// <summary>Resources/ItemDatabase 를 처음 쓸 때 불러온다. 검증 코드는 임시 목록으로 바꿔 끼운다(null 이면 다시 불러온다).</summary>
    public static ItemDatabase Instance
    {
        get { if (instance == null) instance = Resources.Load<ItemDatabase>("ItemDatabase"); return instance; }
        set => instance = value;
    }

    public List<ItemDefinition> items = new();

    public ItemDefinition Find(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        foreach (var item in items)
            if (item != null && string.Equals(item.itemId, itemId, StringComparison.Ordinal)) return item;
        return null;
    }
}
