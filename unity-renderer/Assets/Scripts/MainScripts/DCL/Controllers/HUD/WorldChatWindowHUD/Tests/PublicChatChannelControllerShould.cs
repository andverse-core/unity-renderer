using System;
using System.Collections;
using DCL;
using DCL.Interface;
using NSubstitute;
using NUnit.Framework;
using SocialFeaturesAnalytics;
using UnityEngine;
using UnityEngine.TestTools;

public class PublicChatChannelControllerShould
{
    private const string OWN_USER_ID = "my-user-id";
    private const string TEST_USER_ID = "otherUserId";
    private const string TEST_USER_NAME = "otherUserName";

    private PublicChatChannelController controller;
    private IChannelChatWindowView view;
    private IChatHUDComponentView internalChatView;
    private IChatController chatController;
    private IUserProfileBridge userProfileBridge;
    private IMouseCatcher mouseCatcher;

    [SetUp]
    public void SetUp()
    {
        GivenOwnProfile();
        GivenUser(TEST_USER_ID, TEST_USER_NAME);

        chatController = Substitute.For<IChatController>();
        mouseCatcher = Substitute.For<IMouseCatcher>();
        controller = new PublicChatChannelController(
            chatController,
            Substitute.For<ILastReadMessagesService>(),
            userProfileBridge,
            new DataStore(),
            new RegexProfanityFilter(Substitute.For<IProfanityWordProvider>()),
            Substitute.For<ISocialAnalytics>(),
            mouseCatcher,
            ScriptableObject.CreateInstance<InputAction_Trigger>());

        view = Substitute.For<IChannelChatWindowView>();
        internalChatView = Substitute.For<IChatHUDComponentView>();
        view.ChatHUD.Returns(internalChatView);
        controller.Initialize(view);
    }

    [TearDown]
    public void TearDown()
    {
        controller.Dispose();
    }

    [Test]
    public void AddEntryWhenPrivateMessage()
    {
        var msg = new ChatMessage
        {
            messageType = ChatMessage.Type.PRIVATE,
            body = "test message",
            sender = OWN_USER_ID,
            recipient = TEST_USER_ID,
            timestamp = (ulong) DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        chatController.OnAddMessage += Raise.Event<Action<ChatMessage>>(msg);

        internalChatView.Received(1).AddEntry(Arg.Is<ChatEntryModel>(model =>
            model.messageType == msg.messageType
            && model.bodyText == $"<noparse>{msg.body}</noparse>"
            && model.senderId == msg.sender
            && model.otherUserId == msg.recipient));
    }

    [Test]
    public void AddEntryWhenMessageReceived()
    {
        var msg = new ChatMessage
        {
            messageType = ChatMessage.Type.PUBLIC,
            body = "test message",
            sender = TEST_USER_ID
        };

        chatController.OnAddMessage += Raise.Event<Action<ChatMessage>>(msg);

        internalChatView.Received(1).AddEntry(Arg.Is<ChatEntryModel>(model =>
            model.messageType == msg.messageType
            && model.bodyText == $"<noparse>{msg.body}</noparse>"
            && model.senderId == msg.sender));
    }

    [Test]
    public void FilterMessageWhenIsTooOld()
    {
        var msg = new ChatMessage
        {
            messageType = ChatMessage.Type.PRIVATE,
            body = "test message",
            sender = TEST_USER_ID,
            timestamp = 100
        };

        chatController.OnAddMessage += Raise.Event<Action<ChatMessage>>(msg);

        internalChatView.DidNotReceiveWithAnyArgs().AddEntry(default);
    }

    [Test]
    public void SendPublicMessage()
    {
        internalChatView.OnSendMessage += Raise.Event<Action<ChatMessage>>(new ChatMessage
            {body = "test message", messageType = ChatMessage.Type.PUBLIC});
        chatController.Received(1).Send(Arg.Is<ChatMessage>(c => c.body == "test message"
                                                                 && c.sender == OWN_USER_ID
                                                                 && c.messageType == ChatMessage.Type.PUBLIC));
        internalChatView.Received(1).ResetInputField();
        internalChatView.Received(1).FocusInputField();
    }
    
    [Test]
    public void SendPrivateMessage()
    {
        internalChatView.OnSendMessage += Raise.Event<Action<ChatMessage>>(new ChatMessage
            {body = "test message", messageType = ChatMessage.Type.PRIVATE, recipient = TEST_USER_ID});
        chatController.Received(1).Send(Arg.Is<ChatMessage>(c => c.body == $"/w {TEST_USER_ID} test message"
                                                                 && c.sender == OWN_USER_ID
                                                                 && c.messageType == ChatMessage.Type.PRIVATE
                                                                 && c.recipient == TEST_USER_ID));
        internalChatView.Received(1).ResetInputField();
        internalChatView.Received(1).FocusInputField();
    }
    
    [Test]
    public void ResetInputFieldAndActivatePreviewWhenIsInvalidMessage()
    {
        var isPreviewMode = false;
        controller.OnPreviewModeChanged += b => isPreviewMode = b;
        
        internalChatView.OnSendMessage += Raise.Event<Action<ChatMessage>>(new ChatMessage
            {body = "", messageType = ChatMessage.Type.PUBLIC, recipient = TEST_USER_ID});
        
        internalChatView.Received(1).ResetInputField(true);
        view.Received(1).ActivatePreview();
        internalChatView.Received(1).ActivatePreview();
        Assert.IsTrue(isPreviewMode);
    }

    [Test]
    public void CloseWhenButtonPressed()
    {
        var isViewActive = false;
        view.When(v => v.Show()).Do(info => isViewActive = true);
        view.When(v => v.Hide()).Do(info => isViewActive = false);
        controller.OnClosed += () => isViewActive = false;
        view.IsActive.Returns(info => isViewActive);

        controller.SetVisibility(true);
        Assert.IsTrue(view.IsActive);

        controller.View.OnClose += Raise.Event<Action>();
        Assert.IsFalse(view.IsActive);
    }

    [UnityTest]
    public IEnumerator ReplyToWhisperingSender()
    {
        var msg = new ChatMessage
        {
            body = "test message",
            sender = TEST_USER_ID,
            recipient = OWN_USER_ID,
            messageType = ChatMessage.Type.PRIVATE,
            timestamp = (ulong) DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        yield return null;

        chatController.OnAddMessage += Raise.Event<Action<ChatMessage>>(msg);

        yield return null;

        internalChatView.OnMessageUpdated += Raise.Event<Action<string>>("/r ");

        internalChatView.Received(1).SetInputFieldText($"/w {TEST_USER_NAME} ");
    }

    [Test]
    public void ActivatePreviewModeInstantly()
    {
        var isPreviewMode = false;
        controller.OnPreviewModeChanged += b => isPreviewMode = b;
        controller.SetVisibility(true);
        controller.ActivatePreviewModeInstantly();

        view.Received(1).ActivatePreviewInstantly();
        internalChatView.Received(1).ActivatePreview();
        Assert.IsTrue(isPreviewMode);
    }

    [Test]
    public void ActivatePreviewMode()
    {
        var isPreviewMode = false;
        controller.OnPreviewModeChanged += b => isPreviewMode = b;
        controller.SetVisibility(true);
        controller.ActivatePreview();

        view.Received(1).ActivatePreview();
        internalChatView.Received(1).ActivatePreview();
        Assert.IsTrue(isPreviewMode);
    }

    [Test]
    public void ActivatePreviewModeWhenMouseIsLocked()
    {
        var isPreviewMode = false;
        controller.OnPreviewModeChanged += b => isPreviewMode = b;
        controller.SetVisibility(true);

        mouseCatcher.OnMouseLock += Raise.Event<Action>();

        view.Received(1).ActivatePreview();
        internalChatView.Received(1).ActivatePreview();
        Assert.IsTrue(isPreviewMode);
    }

    [Test]
    public void DeactivatePreviewMode()
    {
        var isPreviewMode = false;
        controller.OnPreviewModeChanged += b => isPreviewMode = b;
        controller.SetVisibility(true);
        controller.DeactivatePreview();

        view.Received(1).DeactivatePreview();
        internalChatView.Received(1).DeactivatePreview();
        Assert.IsFalse(isPreviewMode);
    }

    [Test]
    public void DeactivatePreviewModeWhenInputFieldIsSelected()
    {
        var isPreviewMode = false;
        controller.OnPreviewModeChanged += b => isPreviewMode = b;

        internalChatView.OnInputFieldSelected += Raise.Event<Action>();

        view.Received(1).DeactivatePreview();
        internalChatView.Received(1).DeactivatePreview();
        Assert.IsFalse(isPreviewMode);
    }

    [UnityTest]
    public IEnumerator ActivatePreviewModeAfterSomeTimeWhenInputFieldIsDeselected()
    {
        var isPreviewMode = false;
        controller.OnPreviewModeChanged += b => isPreviewMode = b;
        view.IsFocused.Returns(false);

        internalChatView.OnInputFieldDeselected += Raise.Event<Action>();
        yield return new WaitForSeconds(4f);

        view.Received(1).ActivatePreview();
        internalChatView.Received(1).ActivatePreview();
        Assert.IsTrue(isPreviewMode);
    }

    private void GivenOwnProfile()
    {
        var ownProfileModel = new UserProfileModel
        {
            userId = OWN_USER_ID,
            name = "NO_USER"
        };

        var ownUserProfile = ScriptableObject.CreateInstance<UserProfile>();
        ownUserProfile.UpdateData(ownProfileModel);

        userProfileBridge = Substitute.For<IUserProfileBridge>();
        userProfileBridge.GetOwn().Returns(ownUserProfile);
        userProfileBridge.Get(ownProfileModel.userId).Returns(ownUserProfile);
    }

    private void GivenUser(string userId, string name)
    {
        var testUserProfile = ScriptableObject.CreateInstance<UserProfile>();
        testUserProfile.UpdateData(new UserProfileModel
        {
            userId = userId,
            name = name
        });
        userProfileBridge.Get(userId).Returns(testUserProfile);
    }
}