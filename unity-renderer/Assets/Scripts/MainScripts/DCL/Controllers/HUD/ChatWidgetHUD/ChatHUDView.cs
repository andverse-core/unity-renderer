using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DCL;
using DCL.Helpers;
using DCL.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ChatHUDView : BaseComponentView, IChatHUDComponentView
{
    private const string VIEW_PATH = "SocialBarV1/Chat";

    public TMP_InputField inputField;
    public RectTransform chatEntriesContainer;
    public ScrollRect scrollRect;
    public GameObject messageHoverPanel;
    public GameObject messageHoverGotoPanel;
    public TextMeshProUGUI messageHoverText;
    public TextMeshProUGUI messageHoverGotoText;
    public UserContextMenu contextMenu;
    public UserContextConfirmationDialog confirmationDialog;
    [SerializeField] private DefaultChatEntryFactory defaultChatEntryFactory;
    [SerializeField] private Model model;
    [SerializeField] private InputAction_Trigger nextChatInHistoryInput;
    [SerializeField] private InputAction_Trigger previousChatInHistoryInput;
    
    [NonSerialized] protected List<ChatEntry> entries = new List<ChatEntry>();

    private readonly ChatMessage currentMessage = new ChatMessage();
    private readonly Dictionary<Action, UnityAction<string>> inputFieldSelectedListeners =
        new Dictionary<Action, UnityAction<string>>();
    private readonly Dictionary<Action, UnityAction<string>> inputFieldUnselectedListeners =
        new Dictionary<Action, UnityAction<string>>();

    private bool isLayoutDirty;
    private bool isSortingDirty;

    public event Action<string> OnMessageUpdated;

    public event Action OnShowMenu
    {
        add
        {
            if (contextMenu != null)
                contextMenu.OnShowMenu += value;
        }
        remove
        {
            if (contextMenu != null)
                contextMenu.OnShowMenu -= value;
        }
    }

    public event Action OnInputFieldSelected
    {
        add
        {
            void Action(string s) => value.Invoke();
            inputFieldSelectedListeners[value] = Action;
            inputField.onSelect.AddListener(Action);
        }
        remove
        {
            if (!inputFieldSelectedListeners.ContainsKey(value)) return;
            inputField.onSelect.RemoveListener(inputFieldSelectedListeners[value]);
            inputFieldSelectedListeners.Remove(value);
        }
    }

    public event Action OnInputFieldDeselected
    {
        add
        {
            void Action(string s) => value.Invoke();
            inputFieldUnselectedListeners[value] = Action;
            inputField.onDeselect.AddListener(Action);
        }
        remove
        {
            if (!inputFieldUnselectedListeners.ContainsKey(value)) return;
            inputField.onDeselect.RemoveListener(inputFieldUnselectedListeners[value]);
            inputFieldUnselectedListeners.Remove(value);
        }
    }

    public event Action OnPreviousChatInHistory;
    public event Action OnNextChatInHistory;

    public event Action<ChatMessage> OnSendMessage;

    public int EntryCount => entries.Count;
    public IChatEntryFactory ChatEntryFactory { get; set; }
    public bool IsInputFieldSelected => inputField.isFocused;

    public static ChatHUDView Create()
    {
        var view = Instantiate(Resources.Load<GameObject>(VIEW_PATH)).GetComponent<ChatHUDView>();
        return view;
    }

    public override void Awake()
    {
        base.Awake();
        inputField.onSubmit.AddListener(OnInputFieldSubmit);
        inputField.onSelect.AddListener(OnInputFieldSelect);
        inputField.onDeselect.AddListener(OnInputFieldDeselect);
        inputField.onValueChanged.AddListener(str => OnMessageUpdated?.Invoke(str));
        ChatEntryFactory ??= defaultChatEntryFactory;
    }
    
    public override void OnEnable()
    {
        base.OnEnable();
        UpdateLayout();
        nextChatInHistoryInput.OnTriggered += HandleNextChatInHistoryInput;
        previousChatInHistoryInput.OnTriggered += HandlePreviousChatInHistoryInput;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        nextChatInHistoryInput.OnTriggered -= HandleNextChatInHistoryInput;
        previousChatInHistoryInput.OnTriggered -= HandlePreviousChatInHistoryInput;
    }

    public override void Update()
    {
        base.Update();
        
        if (isLayoutDirty)
            chatEntriesContainer.ForceUpdateLayout(delayed: false);
        isLayoutDirty = false;
        
        if (isSortingDirty)
            SortEntriesImmediate();
        isSortingDirty = false;
    }

    public void ResetInputField(bool loseFocus = false)
    {
        inputField.text = string.Empty;
        inputField.caretColor = Color.white;
        if (loseFocus)
            UnfocusInputField();
    }

    public override void RefreshControl()
    {
        if (model.isInputFieldFocused)
            FocusInputField();
        SetInputFieldText(model.inputFieldText);
        SetFadeoutMode(model.enableFadeoutMode);
        if (model.isPreviewMode)
            ActivatePreview();
        else
            DeactivatePreview();
        ClearAllEntries();
        foreach (var entry in model.entries)
            AddEntry(entry);
    }

    public void FocusInputField()
    {
        inputField.ActivateInputField();
        inputField.Select();
    }

    public void UnfocusInputField() => EventSystem.current?.SetSelectedGameObject(null);

    public void SetInputFieldText(string text)
    {
        model.inputFieldText = text;
        inputField.text = text;
        inputField.MoveTextEnd(false);
    }

    public void ActivatePreview()
    {
        model.isPreviewMode = true;
        
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            entry.ActivatePreview();
        }
    }

    public void DeactivatePreview()
    {
        model.isPreviewMode = false;
        
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            entry.DeactivatePreview();
        }
    }

    private void SetFadeoutMode(bool enabled)
    {
        model.enableFadeoutMode = enabled;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (enabled)
            {
                entry.SetFadeout(IsEntryVisible(entry));
            }
            else
            {
                entry.SetFadeout(false);
            }
        }

        if (enabled)
        {
            confirmationDialog.Hide();
        }
    }

    public virtual void AddEntry(ChatEntryModel model, bool setScrollPositionToBottom = false)
    {
        var chatEntry = ChatEntryFactory.Create(model);
        chatEntry.transform.SetParent(chatEntriesContainer, false);

        if (this.model.enableFadeoutMode && IsEntryVisible(chatEntry))
            chatEntry.SetFadeout(true);
        else
            chatEntry.SetFadeout(false);

        chatEntry.Populate(model);
        
        if (this.model.isPreviewMode)
            chatEntry.ActivatePreviewInstantly();
        else
            chatEntry.DeactivatePreviewInstantly();

        if (model.messageType == ChatMessage.Type.PUBLIC
            || model.messageType == ChatMessage.Type.PRIVATE)
            chatEntry.OnPressRightButton += OnOpenContextMenu;

        chatEntry.OnTriggerHover += OnMessageTriggerHover;
        chatEntry.OnTriggerHoverGoto += OnMessageCoordinatesTriggerHover;
        chatEntry.OnCancelHover += OnMessageCancelHover;
        chatEntry.OnCancelGotoHover += OnMessageCancelGotoHover;

        entries.Add(chatEntry);

        SortEntries();
        UpdateLayout();

        if (setScrollPositionToBottom && scrollRect.verticalNormalizedPosition > 0)
            scrollRect.verticalNormalizedPosition = 0;
    }

    public void RemoveFirstEntry()
    {
        if (entries.Count <= 0) return;
        Destroy(entries[0].gameObject);
        entries.Remove(entries[0]);
    }

    public override void Hide(bool instant = false)
    {
        base.Hide(instant);
        if (contextMenu == null) return;
        contextMenu.Hide();
        confirmationDialog.Hide();
    }

    public void OnMessageCancelHover()
    {
        messageHoverPanel.SetActive(false);
        messageHoverText.text = string.Empty;
    }

    public virtual void ClearAllEntries()
    {
        foreach (var entry in entries)
            Destroy(entry.gameObject);
        entries.Clear();
    }

    private bool IsEntryVisible(ChatEntry entry)
    {
        int visibleCorners =
            (entry.transform as RectTransform).CountCornersVisibleFrom(scrollRect.viewport.transform as RectTransform);
        return visibleCorners > 0;
    }

    private void OnInputFieldSubmit(string message)
    {
        currentMessage.body = message;
        currentMessage.sender = string.Empty;
        currentMessage.messageType = ChatMessage.Type.NONE;
        currentMessage.recipient = string.Empty;

        // A TMP_InputField is automatically marked as 'wasCanceled' when the ESC key is pressed
        if (inputField.wasCanceled)
            currentMessage.body = string.Empty;

        OnSendMessage?.Invoke(currentMessage);
    }

    private void OnInputFieldSelect(string message)
    {
        AudioScriptableObjects.inputFieldFocus.Play(true);
    }

    private void OnInputFieldDeselect(string message)
    {
        AudioScriptableObjects.inputFieldUnfocus.Play(true);
    }

    private void OnOpenContextMenu(DefaultChatEntry chatEntry)
    {
        chatEntry.DockContextMenu((RectTransform) contextMenu.transform);
        contextMenu.transform.parent = transform;
        contextMenu.Show(chatEntry.Model.senderId);
    }

    protected virtual void OnMessageTriggerHover(DefaultChatEntry chatEntry)
    {
        if (contextMenu == null || contextMenu.isVisible)
            return;

        messageHoverText.text = chatEntry.messageLocalDateTime;
        chatEntry.DockHoverPanel((RectTransform) messageHoverPanel.transform);
        messageHoverPanel.SetActive(true);
    }

    private void OnMessageCoordinatesTriggerHover(DefaultChatEntry chatEntry, ParcelCoordinates parcelCoordinates)
    {
        messageHoverGotoText.text = $"{parcelCoordinates} INFO";
        chatEntry.DockHoverPanel((RectTransform) messageHoverGotoPanel.transform);
        messageHoverGotoPanel.SetActive(true);
    }

    private void OnMessageCancelGotoHover()
    {
        messageHoverGotoPanel.SetActive(false);
        messageHoverGotoText.text = string.Empty;
    }

    private void SortEntries() => isSortingDirty = true;

    private void SortEntriesImmediate()
    {
        entries = entries.OrderBy(x => x.Model.timestamp).ToList();

        int count = entries.Count;
        for (int i = 0; i < count; i++)
        {
            if (entries[i].transform.GetSiblingIndex() != i)
                entries[i].transform.SetSiblingIndex(i);
        }
    }
    
    private void HandleNextChatInHistoryInput(DCLAction_Trigger action) => OnNextChatInHistory?.Invoke();
    
    private void HandlePreviousChatInHistoryInput(DCLAction_Trigger action) => OnPreviousChatInHistory?.Invoke();

    private void UpdateLayout() => isLayoutDirty = true;

    [Serializable]
    private struct Model
    {
        public bool isPreviewMode;
        public bool isInputFieldFocused;
        public string inputFieldText;
        public bool enableFadeoutMode;
        public ChatEntryModel[] entries;
    }
}