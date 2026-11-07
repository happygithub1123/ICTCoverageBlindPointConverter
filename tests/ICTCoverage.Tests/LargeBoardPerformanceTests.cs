using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using ICTCoverage.Core;
using ICTCoverage.WinForms;
using Xunit;
namespace ICTCoverage.Tests;
[Collection("WinForms")]
public sealed class LargeBoardPerformanceTests
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern IntPtr GetCurrentThread();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern bool GetThreadTimes(IntPtr thread,out long created,out long exited,out long kernel,out long user);
    static TimeSpan UiCpu(){if(!GetThreadTimes(GetCurrentThread(),out _,out _,out long kernel,out long user))throw new InvalidOperationException("无法读取UI线程CPU时间");return TimeSpan.FromTicks(kernel+user);}
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Private)!.GetValue(o)!;
    [FourProjectsFact]public async Task Mg0335ImportScrollPaintAndIdleCpu()
    {
        Exception? failure=null;var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);Application.EnableVisualStyles();
                using var form=new MainForm(Path.Combine(ConverterTests.Root,"outputs","mg-ui-"+Guid.NewGuid().ToString("N")));form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);
                form.Shown+=async(_,_)=>
                {
                    try
                    {
                        using var fixtures=JsonDocument.Parse(File.ReadAllText(Path.Combine(ConverterTests.Root,"outputs","four-project-fixtures.json")));
                        string path=fixtures.RootElement.EnumerateArray().Single(p=>p.GetProperty("project").GetString()!.Contains("0335")).GetProperty("source").GetString()!;
                        Field<TextBox>(form,"input").Text=path;var watch=Stopwatch.StartNew();await(Task)typeof(MainForm).GetMethod("ReadAsync",Private)!.Invoke(form,null)!;double importMs=watch.Elapsed.TotalMilliseconds;
                        var report=Field<CoverageReport>(form,"report");Assert.Equal(1740,report.Analyses[InspectionKind.Missing].Total);Assert.Equal(1144,report.Analyses[InspectionKind.Missing].Untestable);
                        var table=Field<ReportTable>(form,"grid");Assert.Equal(1740,table.RowCount);using var image=new Bitmap(table.Width,table.Height);table.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                        TimeSpan cpu=UiCpu();long cells=table.PaintedCellCount;var frames=new List<double>();var elapsed=Stopwatch.StartNew();
                        for(int i=0;i<180;i++)
                        {
                            watch.Restart();table.FirstDisplayedScrollingRowIndex=i*3;table.SelectedRowIndex=table.FirstDisplayedScrollingRowIndex;table.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));frames.Add(watch.Elapsed.TotalMilliseconds);await Task.Delay(16);
                        }
                        double activeMs=elapsed.Elapsed.TotalMilliseconds,cpuMs=(UiCpu()-cpu).TotalMilliseconds;
                        cpu=UiCpu();await Task.Delay(500);double idleCpuMs=(UiCpu()-cpu).TotalMilliseconds;
                        string output=Path.Combine(ConverterTests.Root,"outputs","mg0335-performance-"+(Environment.GetEnvironmentVariable("ICT_MG_LABEL")??"latest")+".json");
                        File.WriteAllText(output,JsonSerializer.Serialize(new{importMs,frames=frames.Count,maxFrameMs=frames.Max(),p95FrameMs=frames.Order().ElementAt(171),activeMs,cpuMs,idleCpuMs,paintedCells=table.PaintedCellCount-cells,logicalProcessors=Environment.ProcessorCount,cpuMeasurement="UI thread only"},new JsonSerializerOptions{WriteIndented=true}));
                        Assert.True(frames.Max()<1000);Assert.True(table.PaintedCellCount-cells<180*8*30,"不应绘制整板1740条记录");
                    }
                    catch(Exception ex){failure=ex;}finally{form.Close();}
                };Application.Run(form);
            }
            catch(Exception ex){failure=ex;}finally{done.TrySetResult();}
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();await done.Task.WaitAsync(TimeSpan.FromSeconds(120));if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
