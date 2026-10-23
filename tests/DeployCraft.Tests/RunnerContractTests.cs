using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using DeployCraft.Runner;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>The two serializers must preserve the complete job.json and status.json contracts.</summary>
public sealed class RunnerContractTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-contract-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    public static TheoryData<Type, Type> ContractTypes => new()
    {
        { typeof(RunnerJob), typeof(Job) },
        { typeof(RunnerJobStep), typeof(JobStep) },
        { typeof(RunnerConditions), typeof(JobConditions) },
        { typeof(RunnerConditionGroup), typeof(JobConditionGroup) },
        { typeof(RunnerStatus), typeof(RunStatus) },
        { typeof(RunnerStepResult), typeof(StepResult) },
        { typeof(RunnerNetworkCard), typeof(NetworkCard) },
    };

    [Theory]
    [MemberData(nameof(ContractTypes))]
    public void Contracts_HaveTheSameJsonNamesAndTypes(Type serviceType, Type runnerType)
    {
        // Inspect serializer metadata, not a fixed field list: a one-sided addition must fail too.
        var service = JsonSerializerOptions.Default.GetTypeInfo(serviceType).Properties
            .ToDictionary(p => p.Name, p => WireType(p.PropertyType));
        var runner = runnerType.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttribute<DataMemberAttribute>() != null)
            .ToDictionary(m => m.GetCustomAttribute<DataMemberAttribute>()!.Name ?? m.Name,
                m => WireType(m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType));

        Assert.Equal(service.Keys.Order(), runner.Keys.Order());
        foreach (var (name, type) in service) Assert.Equal(type, runner[name]);
    }

    private static string WireType(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying) return WireType(underlying);
        if (type == typeof(string) || type == typeof(DateTime)) return "string";
        if (type == typeof(int)) return "integer";
        if (type == typeof(bool)) return "boolean";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return $"array<{WireType(type.GetGenericArguments()[0])}>";
        var pair = ContractTypes.FirstOrDefault(p => (Type)p[0] == type || (Type)p[1] == type);
        Assert.NotNull(pair);
        return ((Type)pair[0]).Name;
    }

    [Theory]
    [InlineData(0, "fr")]
    [InlineData(1, "en")]
    [InlineData(2, "fr")]
    [InlineData(3, "en")]
    public void ServiceJob_IsReadByRunner_WithEveryField(int variant, string language)
    {
        var expected = JobSample(variant, language);
        var path = Path.Combine(_dir, "job.json");
        // Same writer as TargetSession.StartTranche, and the runner's actual file reader.
        File.WriteAllText(path, JsonSerializer.Serialize(expected));
        var actual = Json.Read<Job>(path);

        var resultPath = Path.Combine(_dir, "job-read-back.json");
        Json.Write(resultPath, actual);
        AssertJson(JsonSerializer.SerializeToElement(expected), ReadJson(resultPath), "job");
    }

    private static RunnerJob JobSample(int variant, string language)
    {
        if (variant == 3) return new RunnerJob { Language = language };
        var flag = variant == 0;
        var optional = variant == 2;
        return new RunnerJob
        {
            FirstNumber = 7,
            Language = language,
            Conditions = optional ? null : ConditionsSample(variant),
            Steps = new[] { "install", "command", "powershell", "pause", "copyfiles", "reboot", "message" }
                .Select((type, i) => new RunnerJobStep
                {
                    Name = $"Step {i}: été, œuvre — 日本語",
                    Type = type,
                    Command = optional ? null : "echo \"contract\"\r\nnext line",
                    Arguments = optional ? null : "/quiet /norestart",
                    Seconds = 37 + i,
                    SuccessCodes = optional ? [] : [0, 3010, -1],
                    TimeoutSeconds = 123 + i,
                    ContinueOnError = flag,
                    StopSuccessOnError = flag,
                    Disabled = flag,
                    RunAsLoggedOnUser = flag,
                    SourcePath = optional ? null : @"\\SERVEUR-01\Packages\setup.msi",
                    IncludeDirectory = flag,
                    CopyDestination = optional ? null : @"%ProgramData%\Example",
                    CopyPatterns = optional ? null : ["*.txt", "*.xml"],
                    CopySubfolders = flag,
                    CopyOverwrite = flag,
                    ShutdownOnly = flag,
                    WaitForUser = flag,
                    CommandLine = optional ? null : "msiexec /i \"setup.msi\" /qn",
                    PullFiles = optional ? null : [@"\\SERVEUR-01\Packages\config.xml"],
                    KeepFolder = optional ? null : @"%ProgramData%\Example\Installers",
                    AdditionalFiles = optional ? null : ["config.xml", "readme.txt"],
                    Conditions = ConditionsSample(variant),
                    ExtraConditions = optional ? null : [new RunnerConditionGroup
                    {
                        Label = "Nested package: Tools",
                        Conditions = ConditionsSample(variant),
                    }],
                }).ToList(),
        };
    }

    private static RunnerConditions ConditionsSample(int variant) => new()
    {
        Windows = variant == 2 ? [] : ["10", "11", "server", "older"],
        Architecture = variant == 2 ? null : variant == 0 ? "x64" : "x86",
        MinPowerShell = variant == 2 ? null : 5,
        LoggedOn = variant == 2 ? null : variant == 0,
        FilePath = variant == 2 ? null : @"%ProgramFiles%\Example\app.exe",
        FileExists = variant == 2 ? null : variant == 0,
        RegistryKey = variant == 2 ? null : @"HKLM\SOFTWARE\Example",
        RegistryValue = variant == 2 ? null : "Version",
        RegistryExists = variant == 2 ? null : variant == 0,
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RunnerStatus_IsReadByService_WithEveryField(int variant)
    {
        var expected = StatusSample(variant);
        var path = Path.Combine(_dir, "status.json");
        Json.Write(path, expected);
        var actual = TargetSession.ReadStatus(path);

        Assert.NotNull(actual);
        AssertJson(ReadJson(path), JsonSerializer.SerializeToElement(actual), "status");
    }

    private static RunStatus StatusSample(int variant)
    {
        if (variant == 3) return new RunStatus { Network = [] };
        var optional = variant == 2;
        return new RunStatus
        {
            State = variant == 0 ? "running" : "done",
            Error = optional ? null : "Example failure: été, œuvre — 日本語",
            Halt = variant == 0,
            HaltReason = optional ? null : "Package conditions not met",
            Network = optional ? null : [
                new NetworkCard { Mac = "02-00-00-00-00-01", Broadcast = "192.0.2.255" },
                new NetworkCard { Mac = "02-00-00-00-00-02", Broadcast = null },
            ],
            Steps = new[] { "running", "success", "failed", "skipped", "stopped" }
                .Select((state, i) => new StepResult
                {
                    Name = $"Step {i}",
                    State = state,
                    ExitCode = optional ? null : i == 2 ? -1 : 3010,
                    CommandLine = optional ? null : "cmd /c echo \"example\"",
                    Output = optional ? null : "été, œuvre — 日本語\r\nsecond line\tend",
                    Error = optional ? null : "Example step error",
                    StartedUtc = optional ? null : new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567).ToString("o"),
                    EndedUtc = optional ? null : new DateTime(2026, 1, 2, 3, 5, 6, DateTimeKind.Utc).ToString("o"),
                }).ToList(),
        };
    }

    private static JsonElement ReadJson(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static void AssertJson(JsonElement expected, JsonElement actual, string path)
    {
        Assert.True(expected.ValueKind == actual.ValueKind, $"{path}: JSON kinds differ.");
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                Assert.Equal(expected.EnumerateObject().Select(p => p.Name).Order(), actual.EnumerateObject().Select(p => p.Name).Order());
                foreach (var property in expected.EnumerateObject())
                    AssertJson(property.Value, actual.GetProperty(property.Name), $"{path}.{property.Name}");
                break;
            case JsonValueKind.Array:
                Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
                for (var i = 0; i < expected.GetArrayLength(); i++) AssertJson(expected[i], actual[i], $"{path}[{i}]");
                break;
            case JsonValueKind.String:
                // The runner uses round-trip strings; System.Text.Json trims trailing fractional zeros.
                if (path.EndsWith(".startedUtc") || path.EndsWith(".endedUtc"))
                    Assert.Equal(expected.GetDateTimeOffset(), actual.GetDateTimeOffset());
                else Assert.Equal(expected.GetString(), actual.GetString());
                break;
            case JsonValueKind.Number:
                Assert.Equal(expected.GetInt32(), actual.GetInt32());
                break;
        }
    }
}
