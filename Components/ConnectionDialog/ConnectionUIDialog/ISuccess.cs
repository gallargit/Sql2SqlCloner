namespace Microsoft.ConnectionDialog.ConnectionUIDialog
{
    internal interface ISuccess
    {
        // function to be called after successfully testing the connection
        void TestButtonSuccess(string username);
    }
}