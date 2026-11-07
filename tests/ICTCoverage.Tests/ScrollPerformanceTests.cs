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
public sealed class ScrollPerformanceTests
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    static object? Call(object o,string name,params object?[] args)=>o.GetType().GetMethod(name,Private)!.Invoke(o,args);

    [Fact]public async Task ThumbBurstRetainsFullPositionAndHonorsScrollHandlers()
    {
        Exception? failure=null;var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>
        {
            try
            {
                using var bar=new VScrollBar{Maximum=200000,LargeChange=100};int changes=0;
                var gridType=typeof(MainForm).Assembly.GetType("ICTCoverage.WinForms.ResponsiveGrid")!;
                var hookType=gridType.GetNestedType("ScrollHook",BindingFlags.NonPublic)!;
                using var hook=(IDisposable)Activator.CreateInstance(hookType,bar,(Action)(()=>{}))!;
                bar.Scroll+=(_,e)=>{changes++;e.NewValue-=e.NewValue%10;};
                for(int i=0;i<600;i++)hookType.GetMethod("QueuePosition")!.Invoke(hook,[70000+i]);
                Assert.Equal(0,changes);hookType.GetMethod("Flush")!.Invoke(hook,null);Assert.Equal(1,changes);Assert.Equal(70590,bar.Value);
                hookType.GetMethod("QueuePosition")!.Invoke(hook,[180000]);hookType.GetMethod("Cancel")!.Invoke(hook,null);hookType.GetMethod("Flush")!.Invoke(hook,null);Assert.Equal(1,changes);Assert.Equal(70590,bar.Value);
            }
            catch(Exception ex){failure=ex;}finally{completion.TrySetResult();}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]public async Task ContinuousWheelSelectionAndPaintKeepMessageLoopResponsive()
    {
        Exception? failure=null;var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);Application.EnableVisualStyles();
                using var form=new MainForm(Path.Combine(ConverterTests.Root,"outputs","scroll-state-"+Guid.NewGuid().ToString("N")));
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);
                form.Shown+=async(_,_)=>
                {
                    try
                    {
                        Field<TextBox>(form,"input").Text=Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xls").Single();await(Task)Call(form,"ReadAsync")!;
                        var grid=Field<ReportTable>(form,"grid");long paintedBefore=grid.PaintedCellCount;
                        long last=Stopwatch.GetTimestamp();double maxGap=0,maxFrame=0;int beats=0;var frames=new List<double>();
                        using var heartbeat=new System.Windows.Forms.Timer{Interval=20};heartbeat.Tick+=(_,_)=>{long now=Stopwatch.GetTimestamp();maxGap=Math.Max(maxGap,Stopwatch.GetElapsedTime(last,now).TotalMilliseconds);last=now;beats++;};heartbeat.Start();await Task.Delay(50);
                        using var canvas=new Bitmap(grid.Width,grid.Height);
                        var wheel=typeof(ReportTable).GetMethod("OnMouseWheel",Private)!;var burst=Stopwatch.StartNew();
                        for(int i=0;i<600;i++)wheel.Invoke(grid,[new MouseEventArgs(MouseButtons.None,0,100,100,-120)]);
                        double burstMs=burst.Elapsed.TotalMilliseconds;await Task.Delay(70);grid.FirstDisplayedScrollingRowIndex=0;int layouts=0;grid.Layout+=(_,_)=>layouts++;
                        for(int i=0;i<90;i++)
                        {
                            var watch=Stopwatch.StartNew();
                            if(i%3==0)grid.FirstDisplayedScrollingRowIndex=(i*47)%Math.Max(1,grid.RowCount-15);
                            else typeof(ReportTable).GetMethod("OnMouseWheel",Private)!.Invoke(grid,[new MouseEventArgs(MouseButtons.None,0,100,100,i%2==0?120:-120)]);
                            int row=grid.FirstDisplayedScrollingRowIndex;grid.SelectedRowIndex=row;
                            grid.DrawToBitmap(canvas,new Rectangle(Point.Empty,canvas.Size));
                            double elapsed=watch.Elapsed.TotalMilliseconds;frames.Add(elapsed);maxFrame=Math.Max(maxFrame,elapsed);await Task.Delay(16);
                        }
                        var extraReports=new List<int>();string fixtures=Path.Combine(ConverterTests.Root,"outputs","four-project-fixtures.json");
                        if(File.Exists(fixtures))
                        {
                            using var doc=JsonDocument.Parse(File.ReadAllText(fixtures));
                            foreach(var fixture in doc.RootElement.EnumerateArray())
                            {
                                string? path=fixture.GetProperty("source").GetString();if(!File.Exists(path))continue;
                                Field<TextBox>(form,"input").Text=path;await(Task)Call(form,"ReadAsync")!;extraReports.Add(grid.RowCount);
                                for(int i=0;i<10;i++)
                                {
                                    var watch=Stopwatch.StartNew();grid.FirstDisplayedScrollingRowIndex=(i*137)%Math.Max(1,grid.RowCount-15);grid.SelectedRowIndex=grid.FirstDisplayedScrollingRowIndex;grid.DrawToBitmap(canvas,new Rectangle(Point.Empty,canvas.Size));
                                    double elapsed=watch.Elapsed.TotalMilliseconds;frames.Add(elapsed);maxFrame=Math.Max(maxFrame,elapsed);await Task.Delay(16);
                                }
                            }
                        }
                        await Task.Delay(50);heartbeat.Stop();
                        var result=new{burstMs,layouts,extraReportRows=extraReports,frames=frames.Count,maxFrameMs=maxFrame,p95FrameMs=frames.Order().ElementAt((int)(frames.Count*.95)),maxHeartbeatGapMs=maxGap,heartbeatTicks=beats,paintedCells=grid.PaintedCellCount-paintedBefore};
                        string label=Environment.GetEnvironmentVariable("ICT_SCROLL_LABEL")??"latest";File.WriteAllText(Path.Combine(ConverterTests.Root,"outputs","scroll-"+label+".json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
                        Assert.True(beats>10);Assert.True(maxFrame<1500,$"单次滚动/选择/完整绘制阻塞 {maxFrame:F0}ms");Assert.True(maxGap<1500,$"连续滚动时消息循环阻塞 {maxGap:F0}ms");
                    }
                    catch(Exception ex){failure=ex;}finally{form.Close();}
                };Application.Run(form);
            }
            catch(Exception ex){failure=ex;}finally{completion.TrySetResult();}
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();await completion.Task.WaitAsync(TimeSpan.FromSeconds(120));if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
