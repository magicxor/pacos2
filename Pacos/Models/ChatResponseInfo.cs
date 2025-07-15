using Microsoft.Extensions.AI;

namespace Pacos.Models;

public sealed record ChatResponseInfo(string Text, IReadOnlyCollection<DataContent> DataContents);
