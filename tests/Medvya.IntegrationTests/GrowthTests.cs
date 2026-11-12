using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-035: WHO LMS z-scores reproduce WHO's own tables to two decimals, the length position is adjusted as WHO says, the two flags
/// (below the 3rd, crossing two major lines) fire at their lines, a child over 5 gets measurements without centiles, only a clinician reads
/// and only a doctor or nurse records, and the measurements are Observation rows inside their tenant. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class GrowthTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    // Six weeks (42 days) before the harness's Monday, 19 Oct 2026. The harness patient is a girl.
    static readonly DateOnly Born = new(2026, 9, 7);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.BookAsync(10, patient: h.Patient);
        await BornAsync(Born);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    async Task BornAsync(DateOnly born)
    {
        await using var db = sql.Patients(h.Tenant);
        await db.Patients.Where(p => p.Id == h.Patient).ExecuteUpdateAsync(s => s.SetProperty(p => p.DateOfBirth, born));
    }

    HttpClient Client(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    IChildHealth Service(Guid user) => h.Get<IChildHealth>(user);
    Uri Path => new($"/v1/patients/{h.Patient}/growth", UriKind.Relative);

    /// <summary>
    /// The SD columns of WHO's expanded tables (SD4neg … SD4, read from the downloaded files by the script that wrote the CSV, at days 0, 1,
    /// 183, 730, 731, 1461 and 1856): each value's z is its column's SD to two decimals. SD±4 of weight and BMI holds only with the restricted
    /// method beyond ±3, so dropping it fails these rows.
    /// </summary>
    [Theory]
    [InlineData("wfa", "M", 0, 1.701, 2.08, 2.459, 2.881, 3.346, 3.859, 4.419, 5.031, 5.642)]
    [InlineData("wfa", "M", 1, 1.692, 2.065, 2.437, 2.854, 3.317, 3.83, 4.394, 5.013, 5.633)]
    [InlineData("wfa", "M", 183, 4.993, 5.675, 6.357, 7.11, 7.939, 8.852, 9.855, 10.956, 12.057)]
    [InlineData("wfa", "M", 730, 7.59, 8.63, 9.67, 10.838, 12.148, 13.62, 15.272, 17.128, 18.984)]
    [InlineData("wfa", "M", 731, 7.593, 8.634, 9.675, 10.843, 12.155, 13.627, 15.281, 17.139, 18.996)]
    [InlineData("wfa", "M", 1461, 9.767, 11.24, 12.713, 14.404, 16.349, 18.591, 21.181, 24.178, 27.176)]
    [InlineData("wfa", "M", 1856, 10.742, 12.459, 14.175, 16.171, 18.497, 21.218, 24.411, 28.171, 31.931)]
    [InlineData("wfa", "F", 0, 1.671, 2.033, 2.395, 2.794, 3.232, 3.711, 4.23, 4.793, 5.356)]
    [InlineData("wfa", "F", 1, 1.635, 1.994, 2.352, 2.752, 3.196, 3.685, 4.222, 4.81, 5.398)]
    [InlineData("wfa", "F", 183, 4.444, 5.088, 5.733, 6.466, 7.302, 8.254, 9.341, 10.585, 11.828)]
    [InlineData("wfa", "F", 730, 7.094, 8.064, 9.033, 10.159, 11.474, 13.018, 14.841, 17.008, 19.175)]
    [InlineData("wfa", "F", 731, 7.098, 8.068, 9.039, 10.165, 11.481, 13.025, 14.85, 17.019, 19.188)]
    [InlineData("wfa", "F", 1461, 9.441, 10.882, 12.323, 14.031, 16.07, 18.525, 21.508, 25.17, 28.832)]
    [InlineData("wfa", "F", 1856, 10.436, 12.145, 13.854, 15.905, 18.389, 21.429, 25.19, 29.903, 34.616)]
    [InlineData("lhfa", "M", 0, 42.312, 44.205, 46.098, 47.991, 49.884, 51.777, 53.67, 55.564, 57.457)]
    [InlineData("lhfa", "M", 1, 42.481, 44.376, 46.271, 48.165, 50.06, 51.955, 53.85, 55.744, 57.639)]
    [InlineData("lhfa", "M", 183, 59.08, 61.221, 63.362, 65.503, 67.644, 69.784, 71.925, 74.066, 76.207)]
    [InlineData("lhfa", "M", 730, 75.583, 78.638, 81.693, 84.747, 87.802, 90.856, 93.911, 96.966, 100.02)]
    [InlineData("lhfa", "M", 731, 74.904, 77.961, 81.017, 84.074, 87.13, 90.187, 93.243, 96.3, 99.356)]
    [InlineData("lhfa", "M", 1461, 86.551, 90.745, 94.939, 99.133, 103.327, 107.521, 111.715, 115.909, 120.104)]
    [InlineData("lhfa", "M", 1856, 91.819, 96.488, 101.158, 105.827, 110.497, 115.166, 119.836, 124.506, 129.175)]
    [InlineData("lhfa", "F", 0, 41.697, 43.56, 45.422, 47.285, 49.148, 51.01, 52.873, 54.736, 56.598)]
    [InlineData("lhfa", "F", 1, 41.854, 43.72, 45.585, 47.451, 49.317, 51.182, 53.048, 54.914, 56.779)]
    [InlineData("lhfa", "F", 183, 56.683, 58.95, 61.217, 63.484, 65.751, 68.018, 70.285, 72.552, 74.819)]
    [InlineData("lhfa", "F", 730, 73.499, 76.725, 79.95, 83.175, 86.401, 89.626, 92.851, 96.077, 99.302)]
    [InlineData("lhfa", "F", 731, 72.822, 76.049, 79.276, 82.503, 85.73, 88.957, 92.184, 95.411, 98.637)]
    [InlineData("lhfa", "F", 1461, 85.501, 89.809, 94.116, 98.424, 102.731, 107.039, 111.346, 115.654, 119.961)]
    [InlineData("lhfa", "F", 1856, 90.771, 95.562, 100.353, 105.144, 109.935, 114.726, 119.517, 124.308, 129.099)]
    [InlineData("bfa", "M", 0, 9.235, 10.184, 11.133, 12.201, 13.407, 14.773, 16.326, 18.1, 19.874)]
    [InlineData("bfa", "M", 1, 9.147, 10.121, 11.095, 12.182, 13.398, 14.76, 16.29, 18.012, 19.733)]
    [InlineData("bfa", "M", 183, 12.501, 13.624, 14.747, 15.982, 17.342, 18.843, 20.501, 22.336, 24.171)]
    [InlineData("bfa", "M", 730, 11.759, 12.664, 13.57, 14.587, 15.736, 17.042, 18.536, 20.26, 21.983)]
    [InlineData("bfa", "M", 731, 11.952, 12.879, 13.806, 14.846, 16.019, 17.349, 18.868, 20.615, 22.362)]
    [InlineData("bfa", "M", 1461, 11.137, 12.101, 13.065, 14.137, 15.333, 16.67, 18.172, 19.864, 21.556)]
    [InlineData("bfa", "M", 1856, 11.007, 11.943, 12.88, 13.95, 15.184, 16.619, 18.304, 20.307, 22.311)]
    [InlineData("bfa", "F", 0, 9.154, 10.122, 11.091, 12.159, 13.336, 14.636, 16.071, 17.657, 19.242)]
    [InlineData("bfa", "F", 1, 9.049, 10.043, 11.038, 12.127, 13.318, 14.623, 16.05, 17.611, 19.172)]
    [InlineData("bfa", "F", 183, 11.775, 12.961, 14.146, 15.457, 16.909, 18.518, 20.306, 22.293, 24.281)]
    [InlineData("bfa", "F", 730, 11.181, 12.137, 13.092, 14.173, 15.405, 16.817, 18.449, 20.349, 22.248)]
    [InlineData("bfa", "F", 731, 11.408, 12.379, 13.349, 14.445, 15.688, 17.108, 18.74, 20.631, 22.521)]
    [InlineData("bfa", "F", 1461, 10.82, 11.819, 12.818, 13.956, 15.26, 16.767, 18.521, 20.58, 22.64)]
    [InlineData("bfa", "F", 1856, 10.587, 11.635, 12.683, 13.887, 15.28, 16.905, 18.82, 21.097, 23.374)]
    public void TheZScoreReproducesWhosPublishedTables(string table, string sex, int day, double sd4neg, double sd3neg, double sd2neg, double sd1neg, double sd0, double sd1, double sd2, double sd3, double sd4)
    {
        var indicator = table switch { "wfa" => GrowthIndicator.WeightForAge, "lhfa" => GrowthIndicator.LengthForAge, _ => GrowthIndicator.BmiForAge };
        double[] values = [sd4neg, sd3neg, sd2neg, sd1neg, sd0, sd1, sd2, sd3, sd4];
        for (var i = 0; i < values.Length; i++)
            Assert.Equal(i - 4, Math.Round(WhoGrowthStandards.ZScore(indicator, sex == "M", day, values[i]), 2));
    }

    [Fact]
    public void CentilesPositionAndTheLinesFollowWho()
    {
        Assert.Equal(50, WhoGrowthStandards.Centile(0), 6);
        Assert.Equal([3, 15, 50, 85, 97], WhoGrowthStandards.Lines.Select(l => (int)Math.Round(WhoGrowthStandards.Centile(l.Z))));
        Assert.Equal(2.275, WhoGrowthStandards.Centile(-2), 3);
        // Lying up to day 730, standing after: a measurement in the other position moves 0.7 cm; intake's (no position) is taken as measured right.
        Assert.Equal(50.7, WhoGrowthStandards.LengthForTables(50, 730, BodyPosition.Standing), 6);
        Assert.Equal(50, WhoGrowthStandards.LengthForTables(50, 730, BodyPosition.Lying), 6);
        Assert.Equal(87.3, WhoGrowthStandards.LengthForTables(88, 731, BodyPosition.Lying), 6);
        Assert.Equal(88, WhoGrowthStandards.LengthForTables(88, 731, BodyPosition.Standing), 6);
        Assert.Equal(88, WhoGrowthStandards.LengthForTables(88, 10, null), 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => WhoGrowthStandards.ZScore(GrowthIndicator.WeightForAge, true, WhoGrowthStandards.LastDay + 1, 18));
        Assert.Null(WhoGrowthStandards.SignOff);
    }

    /// <summary>SAFETY, mutation check: change "latest &lt; Lines[0].Z" in Flags (for example to Lines[1]) or drop the BelowThird line and these fail.</summary>
    [Fact]
    public void BelowTheThirdIsFlaggedOnTheLatestPointOnly()
    {
        Assert.Equal([GrowthAlert.BelowThird], WhoGrowthStandards.Flags([-1.9]));
        Assert.Empty(WhoGrowthStandards.Flags([-1.88]));      // on the 3rd line is not below it
        Assert.Empty(WhoGrowthStandards.Flags([-1.2]));       // between the 3rd and the 15th
        Assert.Empty(WhoGrowthStandards.Flags([-2.5, -1.5])); // was below, is not now
        Assert.Empty(WhoGrowthStandards.Flags([]));
    }

    /// <summary>SAFETY, mutation check: change "moved &lt;= -2" / "&gt;= 2" in Flags to 3 (or 1), or compare only with the previous point, and these fail.</summary>
    [Fact]
    public void CrossingTwoMajorCentileLinesIsFlaggedEitherWay()
    {
        Assert.Equal([GrowthAlert.CrossedDown], WhoGrowthStandards.Flags([0.2, -1.2]));          // across the 50th and the 15th
        Assert.Empty(WhoGrowthStandards.Flags([0.2, -0.5]));                                     // one line only
        Assert.Equal([GrowthAlert.CrossedDown], WhoGrowthStandards.Flags([0.2, -0.5, -1.2]));    // a slow drift over visits counts
        Assert.Equal([GrowthAlert.CrossedUp], WhoGrowthStandards.Flags([-1.2, 0.0]));            // on the 50th counts as reaching it
        Assert.Empty(WhoGrowthStandards.Flags([-1.2, -0.1]));
        Assert.Equal([GrowthAlert.BelowThird, GrowthAlert.CrossedDown], WhoGrowthStandards.Flags([-0.5, -2.1]));
    }

    [Fact]
    public async Task MeasurementsFromTheChartGetWhoCentilesAndTheWeightVelocity()
    {
        // Expected values computed from WHO's girls' tables (weight 3.2 kg at day 0: z −0.07; 4.0 kg at day 42: z −1.00, the 16th centile).
        await Service(h.Nurse).RecordGrowthAsync(h.Patient, h.Facility, new(Born, 3.2m, 49.1m, BodyPosition.Lying, null));
        var view = await Service(h.Nurse).RecordGrowthAsync(h.Patient, h.Facility, new(SchedulingHarness.Monday, 4.0m, 53.5m, BodyPosition.Lying, 37.0m));
        Assert.Equal((true, false, (string?)null), (view.Child, view.SignedOff, view.NoCentiles));
        Assert.Equal([(Born, 0), (SchedulingHarness.Monday, 42)], view.Measurements.Select(m => (m.TakenOn, m.AgeDays)));
        Assert.Equal((4.0m, 53.5m, (BodyPosition?)BodyPosition.Lying, (decimal?)37.0m), (view.Measurements[1].WeightKg!.Value, view.Measurements[1].LengthCm!.Value, view.Measurements[1].Position, view.Measurements[1].HeadCm));
        var series = view.Series.ToDictionary(s => s.Indicator);
        Assert.Equal([(-0.07m, 47.2m), (-1.00m, 15.9m)], series[GrowthIndicator.WeightForAge].Points.Select(p => (p.Z, p.Centile)));
        Assert.Equal([(-0.03m, 49.0m), (-0.79m, 21.6m)], series[GrowthIndicator.LengthForAge].Points.Select(p => (p.Z, p.Centile)));
        Assert.Equal((13.98m, -0.83m), (series[GrowthIndicator.BmiForAge].Points[^1].Value, series[GrowthIndicator.BmiForAge].Points[^1].Z));
        Assert.All(view.Series, s => Assert.Empty(s.Flags));
        // The 50th line is M and the 3rd ≈ 3.54 kg at 6 weeks (day 43 of WHO's girls' table is M 4.5793 at day 42).
        var lines = series[GrowthIndicator.WeightForAge].Lines;
        Assert.Equal([3, 15, 50, 85, 97], lines.Select(l => l.Centile));
        Assert.All(lines, l => Assert.Equal(61, l.Points.Count));
        Assert.Equal((0, 3.23m), (lines[2].Points[0].AgeDays, lines[2].Points[0].Value));
        Assert.Equal(new WeightVelocityView(0.8m, 42, Born, SchedulingHarness.Monday), view.Velocity);

        // A fall to 3.0 kg later the same day: the day's latest weight counts, below the 3rd and two lines down from birth.
        h.Clock.Advance(TimeSpan.FromHours(1));
        view = await Service(h.Doctor).RecordGrowthAsync(h.Patient, h.Facility, new(SchedulingHarness.Monday, 3.0m, null, null, null));
        var weight = view.Series.Single(s => s.Indicator == GrowthIndicator.WeightForAge);
        Assert.Equal(-3.06m, weight.Points[^1].Z);
        Assert.Equal([GrowthAlert.BelowThird, GrowthAlert.CrossedDown], weight.Flags);
        Assert.Equal(-0.2m, view.Velocity!.ChangeKg);
        await using var db = sql.Encounters(h.Tenant);
        var rows = await db.Observations.Where(o => o.PatientId == h.Patient).ToListAsync();
        Assert.Equal(6, rows.Count); // every reading is a row; nothing overwritten
        Assert.All(rows, o => Assert.Equal((ChartSource.ClinicianVerified, (Guid?)h.Facility), (o.Source, o.FacilityId)));
        Assert.Equal(2, rows.Count(o => o.Code == ObservationCode.Length));
        // An earlier day is stored at that day's noon in the clinic's zone.
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 6, 30, 0, TimeSpan.Zero), rows.Where(o => o.Code == ObservationCode.Weight).Min(o => o.TakenAt));
    }

    [Fact]
    public async Task AChildOverFiveShowsMeasurementsWithoutCentilesAndAnAdultHasNone()
    {
        await BornAsync(SchedulingHarness.Monday.AddYears(-6));
        var view = await Service(h.Nurse).RecordGrowthAsync(h.Patient, h.Facility, new(SchedulingHarness.Monday, 19.5m, 112m, BodyPosition.Standing, null));
        Assert.Equal(("over_five", 1, 0), (view.NoCentiles, view.Measurements.Count, view.Series.Count));
        Assert.Equal(112m, view.Measurements[0].LengthCm);

        await BornAsync(SchedulingHarness.Monday.AddYears(-18));
        Assert.False((await Service(h.Doctor).GrowthAsync(h.Patient, h.Facility)).Child);
        Assert.Equal("not_a_child", (await Assert.ThrowsAsync<ChildHealthConflictException>(() =>
            Service(h.Nurse).RecordGrowthAsync(h.Patient, h.Facility, new(SchedulingHarness.Monday, 70m, null, null, null)))).Code);
    }

    [Fact]
    public async Task ImpossibleOrIncompleteMeasurementsAreRefused()
    {
        async Task<IReadOnlyList<(string, string)>> Refused(RecordGrowth c) =>
            (await Assert.ThrowsAsync<ChildHealthValidationException>(() => Service(h.Nurse).RecordGrowthAsync(h.Patient, h.Facility, c))).Errors;
        var today = SchedulingHarness.Monday;
        Assert.Contains(("takenOn", "required"), await Refused(new(null, 4m, null, null, null)));
        Assert.Contains(("takenOn", "future"), await Refused(new(today.AddDays(1), 4m, null, null, null)));
        Assert.Contains(("takenOn", "before_birth"), await Refused(new(Born.AddDays(-1), 4m, null, null, null)));
        Assert.Contains(("weightKg", "required"), await Refused(new(today, null, null, null, null)));
        Assert.Contains(("weightKg", "out_of_range"), await Refused(new(today, 0.1m, null, null, null)));
        Assert.Contains(("lengthCm", "out_of_range"), await Refused(new(today, null, 5m, BodyPosition.Lying, null)));
        Assert.Contains(("position", "required"), await Refused(new(today, null, 53m, null, null))); // never guessed
        Assert.Contains(("headCm", "out_of_range"), await Refused(new(today, null, null, null, 370m))); // millimetres typed
        await using var db = sql.Encounters(h.Tenant);
        Assert.Empty(await db.Observations.ToListAsync());
    }

    [Fact]
    public async Task OverHttpOnlyAClinicianReadsOnlyADoctorOrNurseRecordsAndAnotherTenantSeesNothing()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(h.Reception).GetAsync(Path)).StatusCode);
        foreach (var user in new[] { h.Reception, h.CareCoordinator })
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(user).PostAsJsonAsync(Path, new { takenOn = "2026-10-19", weightKg = 4.0 })).StatusCode);
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Service(h.SecondDoctor).RecordGrowthAsync(h.Patient, h.Facility, new(SchedulingHarness.Monday, 4m, null, null, null)));

        using var bad = await Client(h.Nurse).PostAsJsonAsync(Path, new { takenOn = "2026-10-19", lengthCm = 53.5 });
        Assert.Equal("required", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("position")[0].GetString());
        using var ok = await Client(h.Nurse).PostAsJsonAsync(Path, new { takenOn = "2026-10-19", weightKg = 4.0, lengthCm = 53.5, position = "Lying", headCm = 37.0 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var card = await Client(h.Doctor).GetFromJsonAsync<JsonElement>(Path);
        Assert.Equal("WeightForAge", card.GetProperty("series")[0].GetProperty("indicator").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Client(h.Doctor).GetAsync(new Uri($"/v1/patients/{Guid.NewGuid()}/growth", UriKind.Relative))).StatusCode);

        await using (var audit = sql.Audit(h.Tenant))
        {
            var events = await audit.Events.Where(e => e.Action == ChildHealthService.GrowthAction || e.Action == ChildHealthService.GrowthReadAction).ToListAsync();
            Assert.Contains(events, e => e.Action == ChildHealthService.GrowthAction && e.PatientId == h.Patient && e.ActorUserId == h.Nurse);
            Assert.Contains(events, e => e.Action == ChildHealthService.GrowthReadAction && e.ActorUserId == h.Doctor);
        }
        await using var foreign = sql.Encounters(Guid.NewGuid());
        Assert.Empty(await foreign.Observations.IgnoreQueryFilters().ToListAsync()); // row-level security alone
        await using var mine = sql.Encounters(h.Tenant);
        Assert.Equal(3, await mine.Observations.CountAsync());
    }
}
