using wa_api.Features.ClinicCrm;
using Xunit;

namespace wa_api.Tests.ClinicCrm;

/// <summary>
/// Observed live: "Charlie" booked, and the clinic's calendar showed "dermalyn", the name the CRM kept from
/// an older chat on that number. The CRM cannot be renamed, so the given name leads the booking notes.
/// </summary>
public class CrmNameGivenTests
{
    [Fact]
    public void DifferentNameOnFile_GivenNameLeadsTheNotes()
    {
        var notes = ClinicCrmBookingService.WithNameGiven("Charlie", "dermalyn", "Concern: hair fall");

        Assert.Equal("Name given for this booking: Charlie (on file as dermalyn)\nConcern: hair fall", notes);
    }

    [Fact]
    public void DifferentNameOnFile_NoNotes_JustTheNameLine()
    {
        Assert.Equal("Name given for this booking: Charlie (on file as dermalyn)",
            ClinicCrmBookingService.WithNameGiven("Charlie", "dermalyn", null));
    }

    [Theory]
    [InlineData("charlie ")]
    [InlineData(null)]
    [InlineData("")]
    public void SameOrNoNameOnFile_NotesUnchanged(string? onFile)
    {
        Assert.Equal("Concern: hair fall", ClinicCrmBookingService.WithNameGiven("Charlie", onFile, "Concern: hair fall"));
    }
}
