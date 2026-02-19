using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Onibox.Services;

public static class AutoStartScheduler
{
    public const string TaskName = "Onibox";
    public const string AutoStartArg = "--autostart";

    public static bool Enable(string exePath)
    {
        return CreateAutoStartTask(exePath);
    }

    public static bool Disable()
    {
        var args = $"/Delete /TN \"{TaskName}\" /F";
        return RunSchtasks(args, ignoreErrors: true);
    }

    private static bool CreateAutoStartTask(string exePath)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"onibox-autostart-{Guid.NewGuid():N}.xml");
        try
        {
            var xml = BuildAutoStartTaskXml(exePath);
            File.WriteAllText(tempPath, xml, Encoding.Unicode);
            var args = $"/Create /TN \"{TaskName}\" /XML \"{tempPath}\" /F";
            return RunSchtasks(args);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }

    private static string BuildAutoStartTaskXml(string exePath)
    {
        var userId = WindowsIdentity.GetCurrent().Name;
        var workingDirectory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = AppContext.BaseDirectory;
        }

        var escapedUserId = EscapeXml(userId);
        var escapedExePath = EscapeXml(exePath);
        var escapedArgs = EscapeXml(AutoStartArg);
        var escapedWorkingDir = EscapeXml(workingDirectory);

        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>{escapedUserId}</Author>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{escapedUserId}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT72H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{escapedExePath}</Command>
      <Arguments>{escapedArgs}</Arguments>
      <WorkingDirectory>{escapedWorkingDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
    }

    private static string EscapeXml(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }

    private static bool RunSchtasks(string arguments, bool ignoreErrors = false)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            if (!process.Start())
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill(true);
                return false;
            }

            if (process.ExitCode == 0)
            {
                return true;
            }

            return ignoreErrors && string.IsNullOrWhiteSpace(error);
        }
        catch
        {
            return false;
        }
    }
}
