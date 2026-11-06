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
public sealed class PerformanceTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Private)!.GetValue(o)!;
    static object? Call(object o,string n,params object?[] args)=>o.GetType().GetMethod(n,Private)!.Invoke(o,args);
    [Fact]public async Task ImportResponsivenessAndRefreshProbe()
    {
        Exception? failure=null;var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);Application.EnableVisualStyles();string data=Path.Combine(ConverterTests.Root,"outputs","perf-state-"+Guid.NewGuid().ToString("N"));using var form=new MainForm(data);form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);
                form.Shown+=async(_,_)=>
                {
                    try
                    {
                        var results=new Dictionary<string,double>();long last=Stopwatch.GetTimestamp();double maxGap=0;int beats=0;using var heartbeat=new System.Windows.Forms.Timer{Interval=20};heartbeat.Tick+=(_,_)=>{long now=Stopwatch.GetTimestamp();maxGap=Math.Max(maxGap,Stopwatch.GetElapsedTime(last,now).TotalMilliseconds);last=now;beats++;};heartbeat.Start();await Task.Delay(50);
                        string path=Directory.GetFiles(Path.Combine(ConverterTests.Root,"samples"),"*.xls").Single();Field<TextBox>(form,"input").Text=path;
                        var watch=Stopwatch.StartNew();await (Task)Call(form,"ReadAsync")!;results["importMs"]=watch.Elapsed.TotalMilliseconds;await Task.Delay(100);results["maxImportHeartbeatGapMs"]=maxGap;results["heartbeatTicks"]=beats;heartbeat.Stop();
                        foreach(string method in new[]{"RefreshResults","RefreshCopy","RefreshLog"}){watch.Restart();Call(form,method);results[method+"Ms"]=watch.Elapsed.TotalMilliseconds;}
                        // A larger in-memory view catches accidental per-row UI work without relying on private sample files.
                        var original=Field<CoverageReport>(form,"report");var large=new CoverageReport{SourceFingerprint="performance-only"};
                        large.Analyses[InspectionKind.Missing]=new Analysis{Components=Enumerable.Range(1,10000).Select(i=>new Component{Sheet="Sheet1",SourceRow=i,Reference="R"+i,RawReference="R"+i,RawValue="1K",Value="1K",Type="电阻",SourceState=TestState.Untestable,State=TestState.Untestable,Issues=[new("classification","原因待确定")]}).ToList()};
                        large.Analyses[InspectionKind.Missing].Components[0].SourceState=TestState.Unknown;large.Analyses[InspectionKind.Missing].Components[0].State=TestState.Unknown;typeof(MainForm).GetField("report",Private)!.SetValue(form,large);watch.Restart();Call(form,"ActivateInspection");results["tenThousandRowViewMs"]=watch.Elapsed.TotalMilliseconds;
                        var grid=Field<DataGridView>(form,"grid");Assert.Equal(10000,grid.Rows.Count);grid.FirstDisplayedScrollingRowIndex=9999;Assert.Equal("R10000",grid.Rows[9999].Cells["元件位置"].Value);
                        watch.Restart();Call(form,"ShowPage","异常待确认");results["tenThousandIssuesMs"]=watch.Elapsed.TotalMilliseconds;Field<CheckBox>(form,"selectAllIssues").Checked=true;
                        Assert.Equal(9999,Field<HashSet<int>>(form,"checkedIssues").Count);var issueGrid=Field<DataGridView>(form,"issues");Assert.Equal(true,issueGrid.Rows[9999].Cells["选择"].Value);
                        issueGrid.CurrentCell=issueGrid.Rows[0].Cells["选择"];Assert.False(issueGrid.BeginEdit(false));issueGrid.Rows[0].Cells["选择"].Value=true;Assert.DoesNotContain(0,Field<HashSet<int>>(form,"checkedIssues"));
                        issueGrid.CurrentCell=issueGrid.Rows[1].Cells["选择"];Assert.True(issueGrid.BeginEdit(false));issueGrid.CurrentCell.Value=false;issueGrid.EndEdit();Assert.DoesNotContain(1,Field<HashSet<int>>(form,"checkedIssues"));
                        Call(form,"ShowPage","分类筛选");Call(form,"ShowPage","异常待确认");Assert.DoesNotContain(1,Field<HashSet<int>>(form,"checkedIssues"));
                        typeof(MainForm).GetField("report",Private)!.SetValue(form,original);Call(form,"ActivateInspection");Call(form,"ShowPage","分类筛选");
                        // Search is coalesced; switching pages must flush the latest filter immediately.
                        Field<TextBox>(form,"refFilter").Text="R103";await Task.Delay(350);Assert.All(grid.Rows.Cast<DataGridViewRow>(),r=>Assert.Contains("R103",r.Cells["元件位置"].Value!.ToString()));Call(form,"ResetFilters");
                        results["rows"]=Field<DataGridView>(form,"grid").Rows.Count;string label=Environment.GetEnvironmentVariable("ICT_PERF_LABEL")??"latest";File.WriteAllText(Path.Combine(ConverterTests.Root,"outputs","performance-"+label+".json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Assert.Equal(1202,results["rows"]);Assert.True(beats>5,"导入时界面消息循环应持续运行");Assert.True(maxGap<1500,$"导入期间主线程停顿 {maxGap:F0}ms，超过1500ms回归上限");Assert.True(results["tenThousandRowViewMs"]<1500,"万行主表刷新不应逐行创建控件");Assert.True(results["tenThousandIssuesMs"]<1500,"万行异常页不应逐行创建控件");
                    }catch(Exception ex){failure=ex;}finally{form.Close();}
                };Application.Run(form);
            }catch(Exception ex){failure=ex;}finally{completion.TrySetResult();}
        });thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();await completion.Task.WaitAsync(TimeSpan.FromSeconds(120));if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
