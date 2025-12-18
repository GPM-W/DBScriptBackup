using Microsoft.SqlServer.Management.Smo;

namespace DBScriptBackup
{
    public static class Util
    {
        public static bool IsSystemObjectGeneric(SqlSmoObject o)
        {
            // 1) Prefer the SMO property bag if it exposes "IsSystemObject"
            try
            {
                var props = o.Properties;
                if (props != null && props.Contains("IsSystemObject"))
                {
                    var val = props["IsSystemObject"].Value;
                    if (val is bool b) return b;
                    if (val != null && bool.TryParse(val.ToString(), out var parsed)) return parsed;
                }
            }
            catch
            {
                // Property may not be available for some objects/versions; ignore and fall back
            }

            // 2) Fallback: many system objects live in these schemas
            if (o is ScriptSchemaObjectBase s)
            {
                var schema = s.Schema;
                if (schema != null &&
                    (schema.Equals("sys", StringComparison.OrdinalIgnoreCase) ||
                     schema.Equals("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            // Otherwise treat as user object
            return false;
        }

    }
}
