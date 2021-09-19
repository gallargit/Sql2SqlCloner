using System.Drawing;

namespace Sql2SqlCloner.Core.Data
{
    public class SqlDataObject
    {
        public Bitmap Status { get; set; }
        public string Table { get; set; }
        public string SqlCommand { get; set; }
        public long TopRecords { get; set; }
        public string WhereFilter { get; set; }
        public bool HasRelationships { get; set; }
        public string Error { get; set; }
    }
}
