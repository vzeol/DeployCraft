using DeployCraft.Server.Data;
using DeployCraft.Server.Validation;

namespace DeployCraft.Tests;

/// <summary>Checks run on packages, schedules and credentials before they are saved.</summary>
public class ValidationTests
{
    private static Package Valid() => new() { Name = "Tool", Steps = [new() { Type = StepType.Command, Command = "hostname" }] };

    [Fact]
    public void Package_Valid_HasNoError() => Assert.Null(PackageValidation.Validate(Valid(), _ => null));

    [Fact]
    public void Package_WithoutName_IsRefused() =>
        Assert.NotNull(PackageValidation.Validate(new Package { Name = " " }, _ => null));

    [Fact]
    public void Package_NullConditions_BecomeEmpty()
    {
        var package = Valid();
        package.Conditions = null!;
        package.Steps[0].Conditions = null!;

        Assert.Null(PackageValidation.Validate(package, _ => null));
        Assert.NotNull(package.Conditions);
        Assert.NotNull(package.Steps[0].Conditions);
    }

    [Fact]
    public void Package_EmptyCommand_NamesTheStep()
    {
        var package = Valid();
        package.Steps.Add(new() { Type = StepType.Command, Command = "" });

        Assert.Contains("2", PackageValidation.Validate(package, _ => null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(86401)]
    public void Package_PauseOutOfRange_IsRefused(int seconds)
    {
        var package = Valid();
        package.Steps[0] = new() { Type = StepType.Pause, PauseSeconds = seconds };

        Assert.NotNull(PackageValidation.Validate(package, _ => null));
    }

    [Theory]
    [InlineData(@"HKLM\Software\Tool", true)]
    [InlineData(@"HKEY_CURRENT_USER\Software", true)]
    [InlineData(@"SOFTWARE\Tool", false)]
    public void Conditions_RegistryKey_MustStartWithAHive(string key, bool valid)
    {
        var conditions = new Conditions { Registry = PresenceCondition.Exists, RegistryKey = key };

        Assert.Equal(valid, PackageValidation.ValidateConditions(conditions) == null);
    }

    [Fact]
    public void Offline_RetryNeedsTwoAttempts() =>
        Assert.NotNull(PackageValidation.ValidateOffline(new OfflineSettings { Retry = true, RetryAttempts = 1 }));

    [Fact]
    public void Schedule_UnknownCredential_IsRefused() =>
        Assert.NotNull(ScheduleValidation.Validate(new Schedule { Name = "Night", CredentialId = 5 }, _ => false));

    [Fact]
    public void Schedule_WeeklyWithoutDays_IsRefused()
    {
        var schedule = new Schedule { Name = "Night", Triggers = [new() { Type = TriggerType.Weekly }] };

        Assert.NotNull(ScheduleValidation.Validate(schedule, _ => true));
    }

    [Fact]
    public void Schedule_Valid_HasNoError()
    {
        var schedule = new Schedule { Name = "Night", CredentialId = 5, Triggers = [new() { Type = TriggerType.Daily }] };

        Assert.Null(ScheduleValidation.Validate(schedule, _ => true));
    }

    [Theory]
    [InlineData(@"DOMAINE\admin")]
    [InlineData("admin@domaine.local")]
    public void Credential_DomainInUserName_IsRefused(string user) =>
        Assert.NotNull(CredentialValidation.Validate(user, "secret", isNew: true));

    [Fact]
    public void Credential_NewWithoutPassword_IsRefused() =>
        Assert.NotNull(CredentialValidation.Validate("admin", null, isNew: true));

    [Fact]
    public void Credential_EditWithoutPassword_KeepsTheStoredOne() =>
        Assert.Null(CredentialValidation.Validate("admin", null, isNew: false));
}
