using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure.LocalAi;

public sealed record ModelAdapterFailure(LocalModelState State, string Message);
