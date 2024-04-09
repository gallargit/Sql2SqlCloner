namespace Sql2SqlCloner.Core.SchemaTransfer
{
    public class SqlSchemaTable : SqlSchemaObject
    {
        public long RowCount { get; set; } = 0;
        public long TopRecords { get; set; } = 0;
        public string WhereFilter { get; set; }
        public string OrderByFields { get; set; }
        public bool HasRelationships { get; set; }
    }
}
