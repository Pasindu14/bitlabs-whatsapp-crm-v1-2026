using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Features.ContactLists.Dtos;

namespace wa_api.Features.ContactLists.Controllers;

/// <summary>
/// Contact-list management for a tenant. COMPANY-SCOPED: every endpoint runs as the caller's
/// company (CompanyId from the JWT) and is restricted to <c>CompanyAdmin</c>.
/// </summary>
[ApiController]
[Route("contact-lists")]
[Authorize(Roles = "CompanyAdmin")]
public class ContactListsController(IContactListService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/contact-lists — paged, searchable list (with member counts).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetPagedAsync(page, pageSize, search, sortBy, sortOrder, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>GET /api/v1/contact-lists/{id} — single list.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/contact-lists — create a list.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateContactListRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/contact-lists/{id} — edit a list.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateContactListRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/contact-lists/{id}/activate — mark active.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var result = await service.ActivateAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/contact-lists/{id}/deactivate — mark inactive (soft-delete).</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var result = await service.DeactivateAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/contact-lists/{id}/contacts — add existing contacts to the list.</summary>
    [HttpPost("{id:guid}/contacts")]
    public async Task<IActionResult> AddContacts(Guid id, AddContactsToListRequest request, CancellationToken ct)
    {
        var result = await service.AddContactsAsync(id, request.ContactIds, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/contact-lists/{id}/contacts/{contactId} — remove a contact from the list.</summary>
    [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
    public async Task<IActionResult> RemoveContact(Guid id, Guid contactId, CancellationToken ct)
    {
        await service.RemoveContactAsync(id, contactId, ct);
        return Ok(ResponseHelper.Ok(new { removed = true }, CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/contact-lists/{id}/import — bulk-add contacts from an uploaded .xlsx
    /// (header columns <c>Phone</c>, <c>Name</c>). Returns a per-row import report.
    /// </summary>
    [HttpPost("{id:guid}/import")]
    public async Task<IActionResult> Import(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BusinessRuleException("IMPORT_EMPTY_FILE", "No file was uploaded.");

        await using var stream = file.OpenReadStream();
        var result = await service.ImportContactsAsync(id, stream, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
