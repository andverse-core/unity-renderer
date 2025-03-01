using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PrivateChatEntry : BaseComponentView, IComponentModelConfig
{
    [SerializeField] internal Button openChatButton;
    [SerializeField] internal PrivateChatEntryModel model;
    [SerializeField] internal TMP_Text userNameLabel;
    [SerializeField] internal TMP_Text lastMessageLabel;
    [SerializeField] internal ImageComponentView picture;
    [SerializeField] internal UnreadNotificationBadge unreadNotifications;
    [SerializeField] internal Button optionsButton;
    [SerializeField] internal GameObject blockedContainer;
    [SerializeField] internal GameObject onlineStatusContainer;
    [SerializeField] internal GameObject offlineStatusContainer;
    [SerializeField] internal RectTransform userContextMenuPositionReference;

    private UserContextMenu userContextMenu;
    private IChatController chatController;
    private ILastReadMessagesService lastReadMessagesService;

    public PrivateChatEntryModel Model => model;

    public event Action<PrivateChatEntry> OnOpenChat;
    
    public static PrivateChatEntry Create()
    {
        return Instantiate(Resources.Load<PrivateChatEntry>("SocialBarV1/WhisperChannelElement"));
    }

    public override void Awake()
    {
        base.Awake();
        optionsButton.onClick.AddListener(() =>
        {
            userContextMenu.Show(model.userId);
            Dock(userContextMenu);
        });
        openChatButton.onClick.AddListener(() => OnOpenChat?.Invoke(this));
    }

    public void Initialize(IChatController chatController,
        UserContextMenu userContextMenu,
        ILastReadMessagesService lastReadMessagesService)
    {
        this.chatController = chatController;
        this.userContextMenu = userContextMenu;
        this.lastReadMessagesService = lastReadMessagesService;
        userContextMenu.OnBlock -= HandleUserBlocked;
        userContextMenu.OnBlock += HandleUserBlocked;
    }
    
    public void Configure(BaseComponentModel newModel)
    {
        model = (PrivateChatEntryModel) newModel;
        RefreshControl();
    }

    public override void RefreshControl()
    {
        userNameLabel.text = model.userName;
        lastMessageLabel.text = model.lastMessage;
        SetBlockStatus(model.isBlocked);
        SetPresence(model.isOnline);
        unreadNotifications.Initialize(chatController, model.userId, lastReadMessagesService);
        
        if (model.imageFetchingEnabled)
            EnableAvatarSnapshotFetching();
        else
            DisableAvatarSnapshotFetching();
    }
    
    private void HandleUserBlocked(string userId, bool blocked)
    {
        if (userId != model.userId) return;
        SetBlockStatus(blocked);
    }

    public void SetBlockStatus(bool isBlocked)
    {
        model.isBlocked = isBlocked;
        blockedContainer.SetActive(isBlocked);
    }

    private void SetPresence(bool isOnline)
    {
        model.isOnline = isOnline;
        onlineStatusContainer.SetActive(isOnline && !model.isBlocked);
        offlineStatusContainer.SetActive(!isOnline && !model.isBlocked);
    }

    private void Dock(UserContextMenu userContextMenu)
    {
        var menuTransform = (RectTransform) userContextMenu.transform;
        menuTransform.pivot = userContextMenuPositionReference.pivot;
        menuTransform.position = userContextMenuPositionReference.position;
    }
    
    public bool IsVisible(RectTransform container)
    {
        if (!gameObject.activeSelf) return false;
        return ((RectTransform) transform).CountCornersVisibleFrom(container) > 0;
    }

    public void EnableAvatarSnapshotFetching()
    {
        if (model.imageFetchingEnabled) return;
        picture.Configure(new ImageComponentModel {uri = model.pictureUrl});
        model.imageFetchingEnabled = true;
    }

    public void DisableAvatarSnapshotFetching()
    {
        if (!model.imageFetchingEnabled) return;
        picture.SetImage((string) null);
        model.imageFetchingEnabled = false;
    }

    [Serializable]
    public class PrivateChatEntryModel : BaseComponentModel
    {
        public string userId;
        public string userName;
        public string lastMessage;
        public ulong lastMessageTimestamp;
        public string pictureUrl;
        public bool isBlocked;
        public bool isOnline;
        public bool imageFetchingEnabled;

        public PrivateChatEntryModel(string userId, string userName, string lastMessage, string pictureUrl, bool isBlocked, bool isOnline,
            ulong lastMessageTimestamp)
        {
            this.userId = userId;
            this.userName = userName;
            this.lastMessage = lastMessage;
            this.pictureUrl = pictureUrl;
            this.isBlocked = isBlocked;
            this.isOnline = isOnline;
            this.lastMessageTimestamp = lastMessageTimestamp;
        }
    }
}