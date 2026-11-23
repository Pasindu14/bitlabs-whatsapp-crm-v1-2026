using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Dtos;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Templates.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Conversations;

/// <summary>
/// "New chat" with an approved template: the only way WhatsApp lets a business start a conversation with
/// someone outside the 24-hour window. Pins what gets sent, what is refused before sending, and that a
/// refusal leaves no thread behind.
/// </summary>
public class StartConversationTemplateTests
{
    private sealed class Tenant(Guid companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin => false;
    }

    /// <summary>Records template sends instead of calling Meta; persists the Message like the real sender.</summary>
    private sealed class TemplateSpy(AppDbContext db) : IWhatsAppMessageSender
    {
        public List<(string Name, string Language, IReadOnlyList<string> Parameters, string Rendered)> Sent { get; } = [];
        public int TextSends { get; private set; }

        public Task<Message> SendAsync(
            Contact contact, WabaConnection waba, string body, Guid? conversationId,
            bool chargeCredit, CancellationToken ct = default)
        {
            TextSends++;
            return Persist(contact, waba, body, conversationId, ct);
        }

        public Task<Message> SendTemplateAsync(
            Contact contact, WabaConnection waba, string templateName, string language,
            IReadOnlyList<string> parameters, string renderedBody, Guid? conversationId,
            CancellationToken ct = default)
        {
            Sent.Add((templateName, language, parameters, renderedBody));
            return Persist(contact, waba, renderedBody, conversationId, ct);
        }

        private async Task<Message> Persist(
            Contact contact, WabaConnection waba, string body, Guid? conversationId, CancellationToken ct)
        {
            var message = new Message
            {
                Id = Guid.NewGuid(),
                CompanyId = contact.CompanyId,
                ContactId = contact.Id,
                WabaConnectionId = waba.Id,
                ConversationId = conversationId,
                Body = body,
                Direction = MessageDirection.Outbound,
                Status = MessageStatus.Accepted,
                ExternalMessageId = "wamid-" + Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
            };
            db.Messages.Add(message);
            await db.SaveChangesAsync(ct);
            return message;
        }
    }

    private sealed class NoopNotifier : IChatNotifier
    {
        public Task MessageAsync(Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task MessageDeletedAsync(Guid companyId, Guid conversationId, Guid messageId,
            ConversationResponse conversation, CancellationToken ct = default) => Task.CompletedTask;
        public Task BotStateChangedAsync(Guid companyId, ConversationResponse conversation,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task BotTypingAsync(Guid companyId, Guid conversationId, bool isTyping,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task MessageStatusAsync(Guid companyId, Guid conversationId, Guid messageId, MessageStatus status,
            DateTime? statusAt, string? errorMessage, CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly Guid _companyId = Guid.NewGuid();
    private readonly AppDbContext _db;
    private readonly Contact _contact;
    private readonly WabaConnection _waba;
    private readonly TemplateSpy _sender;
    private readonly ConversationService _svc;

    public StartConversationTemplateTests()
    {
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"start-tpl-{Guid.NewGuid()}").Options,
            new Tenant(_companyId));
        _contact = new Contact
        {
            Id = Guid.NewGuid(), CompanyId = _companyId, Phone = "94775875597", Name = "Nimal Perera",
            IsWhatsAppValid = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true,
        };
        _waba = NewWaba();
        _db.AddRange(_contact, _waba);
        _db.SaveChanges();
        _sender = new TemplateSpy(_db);
        _svc = new ConversationService(_db, _sender, new NoopNotifier(), new HttpContextAccessor());
    }

    private WabaConnection NewWaba() => new()
    {
        Id = Guid.NewGuid(), CompanyId = _companyId, PhoneNumberId = "pn-" + Guid.NewGuid(), WabaId = "waba-1",
        DisplayPhoneNumber = "+94 74 415 4100", EncryptedAccessToken = "token", Status = WabaConnectionStatus.Connected,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true,
    };

    private Template AddTemplate(string body, TemplateStatus status = TemplateStatus.Approved, Guid? wabaId = null)
    {
        var t = new Template
        {
            Id = Guid.NewGuid(), CompanyId = _companyId, WabaConnectionId = wabaId ?? _waba.Id,
            Name = "test_template_1", Language = "en", Status = status,
            Components = new TemplateComponents { Body = new TemplateBody { Text = body } },
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true,
        };
        _db.Templates.Add(t);
        _db.SaveChanges();
        return t;
    }

    [Fact] // The whole point: a cold contact can be reached, with every variable filled in order.
    public async Task ApprovedTemplate_ToColdContact_IsSentFilledAndOpensTheThread()
    {
        var t = AddTemplate("Hi {{1}}, your {{2}} is ready.");

        var conv = await _svc.StartConversationAsync(_contact.Id, _waba.Id, null, t.Id, ["Nimal", "report"]);

        var sent = Assert.Single(_sender.Sent);
        Assert.Equal("test_template_1", sent.Name);
        Assert.Equal(["Nimal", "report"], sent.Parameters);
        Assert.Equal("Hi Nimal, your report is ready.", sent.Rendered);
        Assert.Equal(0, _sender.TextSends);
        Assert.Equal("Hi Nimal, your report is ready.", conv.LastMessageBody);
        Assert.Single(_db.Conversations.IgnoreQueryFilters());
    }

    [Fact] // A template with no variables needs no values.
    public async Task TemplateWithoutVariables_SendsItsTextAsIs()
    {
        var t = AddTemplate("Hello! How are you?");

        await _svc.StartConversationAsync(_contact.Id, _waba.Id, null, t.Id, null);

        var sent = Assert.Single(_sender.Sent);
        Assert.Empty(sent.Parameters);
        Assert.Equal("Hello! How are you?", sent.Rendered);
    }

    [Theory]
    [InlineData(TemplateStatus.Pending)]
    [InlineData(TemplateStatus.Rejected)]
    [InlineData(TemplateStatus.Draft)]
    public async Task UnapprovedTemplate_IsRefusedAndLeavesNoThread(TemplateStatus status)
    {
        var t = AddTemplate("Hi {{1}}", status);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _svc.StartConversationAsync(_contact.Id, _waba.Id, null, t.Id, ["Nimal"]));

        Assert.Equal("TEMPLATE_NOT_APPROVED", ex.ErrorCode);
        Assert.Empty(_sender.Sent);
        Assert.Empty(_db.Conversations.IgnoreQueryFilters());
    }

    [Fact] // Meta ties a template to the account it was approved under; another number can't send it.
    public async Task TemplateFromAnotherNumber_IsRefused()
    {
        var other = NewWaba();
        _db.WabaConnections.Add(other);
        _db.SaveChanges();
        var t = AddTemplate("Hi {{1}}", wabaId: other.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _svc.StartConversationAsync(_contact.Id, _waba.Id, null, t.Id, ["Nimal"]));

        Assert.Equal("TEMPLATE_WRONG_NUMBER", ex.ErrorCode);
        Assert.Empty(_sender.Sent);
    }

    [Fact] // A blank value can't be guessed at; refuse it before spending a send.
    public async Task BlankValue_IsRefused()
    {
        var t = AddTemplate("Hi {{1}}, see {{2}}");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _svc.StartConversationAsync(_contact.Id, _waba.Id, null, t.Id, ["Nimal", "  "]));

        Assert.Equal("TEMPLATE_VALUES_INVALID", ex.ErrorCode);
        Assert.Empty(_sender.Sent);
    }

    [Fact] // Meta rejects line breaks in a parameter (132018); they become spaces instead.
    public async Task LineBreakInValue_IsSentAsASpace()
    {
        var t = AddTemplate("Hi {{1}}, see {{2}}");

        var withLineBreak = "line" + (char)10 + "break";
        await _svc.StartConversationAsync(_contact.Id, _waba.Id, null, t.Id, ["Nimal", withLineBreak]);

        Assert.Equal(["Nimal", "line break"], Assert.Single(_sender.Sent).Parameters);
    }

    [Fact] // Inside an open window, plain text is still allowed and reuses the live thread.
    public async Task FreeText_InsideOpenWindow_IsSent()
    {
        _db.Conversations.Add(new Conversation
        {
            Id = Guid.NewGuid(), CompanyId = _companyId, ContactId = _contact.Id, WabaConnectionId = _waba.Id,
            Status = ConversationStatus.Open, LastMessageAt = DateTime.UtcNow, WindowExpiresAt = DateTime.UtcNow.AddHours(5),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true,
        });
        _db.SaveChanges();

        await _svc.StartConversationAsync(_contact.Id, _waba.Id, "following up", null, null);

        Assert.Equal(1, _sender.TextSends);
        Assert.Single(_db.Conversations.IgnoreQueryFilters());
    }

    [Fact]
    public async Task NeitherTextNorTemplate_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _svc.StartConversationAsync(_contact.Id, _waba.Id, "   ", null, null));

        Assert.Equal("MESSAGE_REQUIRED", ex.ErrorCode);
    }
}
