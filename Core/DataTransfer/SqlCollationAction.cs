namespace Sql2SqlCloner.Core.DataTransfer
{
    public enum SqlCollationAction
    {
        Ignore_collation = 0,
        No_collation = 1,
        Keep_source_db_collation = 2,
        Set_destination_db_collation = 3
    }
}
