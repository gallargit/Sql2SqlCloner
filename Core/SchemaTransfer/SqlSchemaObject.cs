using Microsoft.SqlServer.Management.Smo;
using System.Drawing;
using System.Linq;

namespace Sql2SqlCloner.Core.SchemaTransfer
{
    public class SqlSchemaObject
    {
        public Bitmap Status { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool CopyData { get; set; }
        public NamedSmoObject Object { get; set; }
        public string Error { get; set; }
        [System.ComponentModel.Browsable(false)]
        public SqlSchemaObject Parent { get; set; }
        [System.ComponentModel.Browsable(false)]
        public string NameWithBrackets
        {
            get
            {
                return AddBrackets(Name);
            }
        }
        [System.ComponentModel.Browsable(false)]
        public string NameWithoutBrackets
        {
            get
            {
                return Name.Replace("[", "").Replace("]", "");
            }
        }

        public static string AddBrackets(string itemname)
        {
            if (string.IsNullOrEmpty(itemname))
            {
                return "";
            }

            var nameSplit = itemname.Split('.').ToList();
            if (nameSplit.Count < 2)
            {
                return $"[{itemname}]";
            }
            else
            {
                return $"[{nameSplit[0]}].[{string.Join(".", nameSplit.Skip(1))}]";
            }
        }
    }
}
