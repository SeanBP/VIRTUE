using System.Collections.Generic;

namespace VirtueCore.Shared
{
    public static class VersionCheck
    {
        public static bool IsCompatible(string fileVersion, string targetVersion, IEnumerable<string> compatibleVersions)
        {
            return string.Equals(fileVersion, targetVersion) ||
                (compatibleVersions != null && new List<string>(compatibleVersions).Contains(fileVersion));
        }
    }
}
