using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mermaider.Services;

public class MermaidService
{
    private readonly string _mmdcPath;
    private readonly string _toolsDir;

    public MermaidService()
    {
        var appDir = AppContext.BaseDirectory;
        var embeddedToolsDir = EmbeddedToolsService.EnsureExtractedTools();
        
        var possiblePaths = new[]
        {
            !string.IsNullOrEmpty(embeddedToolsDir) ? Path.Combine(embeddedToolsDir, "mmdc.cmd") : null,
            Path.Combine(appDir, "..", "..", "..", "tools", "mmdc.cmd"),
            Path.Combine(appDir, "..", "..", "..", "..", "..", "tools", "mmdc.cmd"),
            Path.Combine(appDir, "tools", "mmdc.cmd"),
        };

        _mmdcPath = possiblePaths.FirstOrDefault(path => !string.IsNullOrEmpty(path) && File.Exists(path))
            ?? Path.Combine(appDir, "tools", "mmdc.cmd");
        _toolsDir = Path.GetDirectoryName(_mmdcPath) ?? "";
    }

    public async Task<byte[]?> RenderToPngAsync(string mermaidCode, double scale = 3.0)
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"mermaid_input_{Guid.NewGuid()}.mmd");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"mermaid_output_{Guid.NewGuid()}.png");

        try
        {
            await File.WriteAllTextAsync(tempInput, mermaidCode);

            var startInfo = new ProcessStartInfo
            {
                FileName = _mmdcPath,
                Arguments = $"-i \"{tempInput}\" -o \"{tempOutput}\" -s {scale} -b transparent",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _toolsDir
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = errorBuilder.ToString();
                if (!string.IsNullOrEmpty(error))
                {
                    throw new Exception($"Mermaid 渲染错误: {error}");
                }
                return null;
            }

            if (File.Exists(tempOutput))
            {
                return await File.ReadAllBytesAsync(tempOutput);
            }

            return null;
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    public async Task<(bool Success, string? Error)> ValidateSyntaxAsync(string mermaidCode)
    {
        if (string.IsNullOrWhiteSpace(mermaidCode))
        {
            return (true, null);
        }

        var tempInput = Path.Combine(Path.GetTempPath(), $"mermaid_validate_{Guid.NewGuid()}.mmd");

        try
        {
            await File.WriteAllTextAsync(tempInput, mermaidCode);

            var startInfo = new ProcessStartInfo
            {
                FileName = _mmdcPath,
                Arguments = $"-i \"{tempInput}\" --outputFormat png",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _toolsDir
            };

            using var process = new Process { StartInfo = startInfo };
            var errorBuilder = new StringBuilder();

            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = errorBuilder.ToString();
                return (false, error);
            }

            return (true, null);
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
        }
    }
}
