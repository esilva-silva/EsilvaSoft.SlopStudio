using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public interface IEnvironmentVaultRepository
{
    EnvironmentVault LoadEnvironments();
    void SaveEnvironments(EnvironmentVault vault);
}
