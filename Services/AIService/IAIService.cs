using System.Collections.Generic;
using System.Threading.Tasks;
using Mermaider.Models;

namespace Mermaider.Services.AIService;

public interface IAIService
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    Task<AIMessage> GenerateAsync(string prompt, string? currentCode, List<AIMessage> history);
}
