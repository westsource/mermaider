using System;
using System.Threading;
using System.Threading.Tasks;
using Mermaider.Models;

namespace Mermaider.Services;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdateAsync();
    Task DownloadUpdateAsync(string downloadUrl, string destinationPath, IProgress<double> progress, CancellationToken cancellationToken);
    string GetCurrentVersion();
}
