using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Represents a single clickable slot in the Inventory UI.
/// DO NOT manually add this script — InventoryPanelUI will add it automatically to each slot.
/// </summary>
public class InventorySlotUI : MonoBehaviour, IPointerClickHandler
{
    [HideInInspector] public Image IconImage;
    [HideInInspector] public Text NameText;
    [HideInInspector] public Text CountText;
    [HideInInspector] public int SlotIndex = -1;
    [HideInInspector] public InventoryPanelUI ParentPanel;

    public void Refresh(ItemData item, int count, bool isSelected)
    {
        if (item != null)
        {
            if (IconImage != null)
            {
                if (item.Icon != null)
                {
                    IconImage.sprite = item.Icon;
                    IconImage.color = Color.white;
                    IconImage.gameObject.SetActive(true);
                }
                else
                {
                    IconImage.sprite = null;
                    IconImage.color = new Color(1f, 1f, 1f, 0f);
                    IconImage.gameObject.SetActive(false);
                }
            }
            if (NameText != null)
            {
                NameText.text = (item.Icon == null) ? item.ItemName : "";
            }
            if (CountText != null)
            {
                CountText.text = count > 1 ? count.ToString() : "";
                CountText.gameObject.SetActive(item != null && count > 1);
                CountText.transform.SetAsLastSibling();
            }
        }
        else
        {
            if (IconImage != null)
            {
                IconImage.sprite = null;
                IconImage.color = new Color(1f, 1f, 1f, 0f);
                IconImage.gameObject.SetActive(false);
            }
            if (NameText != null) NameText.text = "";
            if (CountText != null)
            {
                CountText.text = "";
                CountText.gameObject.SetActive(false);
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ParentPanel != null && SlotIndex >= 0)
        {
            ParentPanel.OnSlotClicked(SlotIndex, eventData.button);
        }
    }
}
