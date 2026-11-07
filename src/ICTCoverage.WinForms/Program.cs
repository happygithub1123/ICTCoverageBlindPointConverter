namespace ICTCoverage.WinForms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_,e)=>MessageBox.Show(e.Exception.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Error);
        using(var login=new LoginForm())if(login.ShowDialog()!=DialogResult.OK)return;
        Application.Run(new MainForm());
    }
}
