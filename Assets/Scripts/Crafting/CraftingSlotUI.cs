using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CraftingSlotUI : MonoBehaviour, IPointerClickHandler
{
    [HideInInspector] public Image IconImage;
    [HideInInspector] public Text NameText;
    [HideInInspector] public Text CountText;
    [HideInInspector] public int SlotIndex = -1;
    [HideInInspector] public CraftingPanelUI ParentPanel;

    public void Refresh(ItemData item, int count)
    {
        if (item != null)
        {
            if (IconImage != null)
            {
                IconImage.sprite = item.Icon;
                IconImage.color = item.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                IconImage.gameObject.SetActive(item.Icon != null);
            }

            if (NameText != null) NameText.text = item.Icon == null ? item.ItemName : "";
            if (CountText != null)
            {
                CountText.text = count > 1 ? count.ToString() : "";
                CountText.gameObject.SetActive(count > 1);
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
        if (ParentPanel != null)
        {
            ParentPanel.OnCraftingSlotClicked(SlotIndex, eventData.button);
        }
    }
}
