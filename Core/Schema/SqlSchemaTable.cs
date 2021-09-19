namespace Sql2SqlCloner.Core.Schema
{
    public class SqlSchemaTable : SqlSchemaObject
    {
        public long RowCount { get; set; }
        public long TopRecords { get; set; }
        public string WhereFilter { get; set; }
        public bool HasRelationships { get; set; }

        public SqlSchemaTable()
        {
            HasRelationships = false;
            RowCount = TopRecords = 0;
            WhereFilter = null;
        }
    }
}
