using Microsoft.SqlServer.Management.Smo;
using System.Drawing;

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
    }
}
