using System.Text;
using VRRecorder.Compliance.Distribution;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsHardwareValidationReportReaderTests
{
    [Fact]
    public void RepositorySchemaIsStrictV1AndHasNoPassedProperty()
    {
        var schema = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "schemas",
            "windows-hardware-validation-report-v1.schema.json"));
        using var document = System.Text.Json.JsonDocument.Parse(schema);

        Assert.False(document.RootElement
            .GetProperty("additionalProperties")
            .GetBoolean());
        Assert.Equal(
            1,
            document.RootElement
                .GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetInt32());
        Assert.DoesNotContain("\"passed\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictV1ReportReadsWithoutAnySelfReportedPassedFlag()
    {
        var report = WindowsHardwareValidationReportReader.Read(Bytes());

        Assert.Equal(1, report.SchemaVersion);
        Assert.Equal(
            "full-production-hardware-validation-v1",
            report.MatrixProfile);
        Assert.Equal(new string('a', 64), report.PayloadIdentitySha256);
        var run = Assert.Single(report.Runs);
        Assert.Equal(
            Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab"),
            run.RunId);
        Assert.Equal("local-hil-runner", run.RunnerId);
        Assert.Equal("windows-11", run.Environment.OperatingSystem.Profile);
        Assert.Equal("nvidia", run.Environment.Gpu.Vendor);
        Assert.Equal(HardwareEncoderMode.Hardware, run.Environment.Encoder.Mode);
        Assert.Equal(HardwareEncoderApi.Nvenc, run.Environment.Encoder.Api);
        var testCase = Assert.Single(run.Cases);
        Assert.Equal("launch-first-run-legal", testCase.Id);
        Assert.Equal(HardwareValidationCaseStatus.Pass, testCase.Status);
        Assert.Single(testCase.Artifacts);
        Assert.Matches("^[0-9a-f]{64}$", report.ReportSha256);
    }

    [Fact]
    public void UnknownVersionPropertyAndSelfReportedPassedAreRejected()
    {
        var json = Json();

        AssertInvalid(json.Replace(
            "\"schemaVersion\": 1",
            "\"schemaVersion\": 2",
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "\"matrixProfile\":",
            "\"unknown\": true,\n  \"matrixProfile\":",
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "\"payloadIdentitySha256\":",
            "\"passed\": true,\n  \"payloadIdentitySha256\":",
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "\"runnerId\": \"local-hil-runner\",",
            "\"runnerId\": \"local-hil-runner\",\n" +
            "      \"runnerId\": \"local-hil-runner\",",
            StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownStatusAndDuplicateRunCaseOrArtifactAreRejected()
    {
        var json = Json();

        AssertInvalid(json.Replace(
            "\"status\": \"pass\"",
            "\"status\": \"unknown\"",
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "    }\n  ]",
            "    },\n" +
            Json().Split("\"runs\": [", StringSplitOptions.None)[1]
                .Split("\n  ]", StringSplitOptions.None)[0] +
            "\n  ]",
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "      ]\n    }",
            "      ,{\n" +
            "        \"id\": \"launch-first-run-legal\",\n" +
            "        \"status\": \"pass\",\n" +
            "        \"artifacts\": [{\n" +
            "          \"path\": \"runs/second.json\",\n" +
            "          \"length\": 1,\n" +
            $"          \"sha256\": \"{new string('c', 64)}\",\n" +
            "          \"kind\": \"diagnostic\"\n" +
            "        }]\n" +
            "      }]\n    }",
            StringComparison.Ordinal));
        AssertInvalid(json.Replace(
            "            }\n          ]",
            "            },\n            {\n" +
            "              \"path\": \"runs/launch.json\",\n" +
            "              \"length\": 1,\n" +
            $"              \"sha256\": \"{new string('c', 64)}\",\n" +
            "              \"kind\": \"diagnostic\"\n" +
            "            }\n          ]",
            StringComparison.Ordinal));
    }

    private static byte[] Bytes() => Encoding.UTF8.GetBytes(Json());

    private static string Json() => $$"""
        {
          "schemaVersion": 1,
          "matrixProfile": "full-production-hardware-validation-v1",
          "payloadIdentitySha256": "{{new string('a', 64)}}",
          "runs": [
            {
              "runId": "01234567-89ab-4cde-8fab-0123456789ab",
              "runnerId": "local-hil-runner",
              "capturedAtUtc": "2026-07-17T00:00:00Z",
              "environment": {
                "os": {
                  "profile": "windows-11",
                  "build": "10.0.26100.4652",
                  "architecture": "x64"
                },
                "gpu": {
                  "vendor": "nvidia",
                  "deviceId": "10de:2684",
                  "driverVersion": "32.0.15.7652"
                },
                "encoder": {
                  "mode": "hardware",
                  "api": "nvenc",
                  "name": "NVIDIA NVENC H.264"
                },
                "steamVr": {
                  "runtimeVersion": "2.10.0",
                  "hmdModel": "PICO 4",
                  "leftController": "PICO Controller",
                  "rightController": "PICO Controller"
                }
              },
              "cases": [
                {
                  "id": "launch-first-run-legal",
                  "status": "pass",
                  "artifacts": [
                    {
                      "path": "runs/launch.json",
                      "length": 123,
                      "sha256": "{{new string('b', 64)}}",
                      "kind": "diagnostic"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static void AssertInvalid(string json) =>
        Assert.Throws<InvalidDataException>(() =>
            WindowsHardwareValidationReportReader.Read(
                Encoding.UTF8.GetBytes(json)));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VR-Recorder.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
