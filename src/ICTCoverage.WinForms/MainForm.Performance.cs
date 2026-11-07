using ICTCoverage.Core;

namespace ICTCoverage.WinForms;

public sealed partial class MainForm
{
    readonly System.Windows.Forms.Timer filterDelay=new(){Interval=180};
    readonly HashSet<string> dirtyPages=[];
    string currentPage="分类筛选";
    object?[][] mainRows=[];
    readonly HashSet<int> checkedIssues=[];
    CoverageReport? cachedReport;RuleConfig? cachedRules;
    readonly Dictionary<int,List<DisplayRow>> viewCache=[];
    sealed record DisplayRow(string Type,string Reference,string RawReference,string Value,string Reason,TestState State,bool Pending,bool PendingCause,object?[] Cells);

    List<DisplayRow> CurrentView()
    {
        if(!ReferenceEquals(cachedReport,report)||!ReferenceEquals(cachedRules,cfg)){viewCache.Clear();cachedReport=report;cachedRules=cfg;}
        int key=selectedKind.HasValue?(int)selectedKind.Value+1:0;
        if(!viewCache.TryGetValue(key,out var result))
        {
            result=ReportView.Scope(report!,selectedKind,new(OnlyUntestable:false)).Select(r=>
            {
                string reason=r.Reasons(cfg);var a=r.Anchor;
                return new DisplayRow(a.Type,a.Reference,a.RawReference,a.Value,reason,r.State,r.Pending,r.PendingCause(cfg),[a.GroupCategory,a.Reference,a.Value,r.Symbol(InspectionKind.Missing),r.Symbol(InspectionKind.Short),r.Symbol(InspectionKind.Wrong),r.Symbol(InspectionKind.OpenSolder),reason]);
            }).ToList();viewCache[key]=result;
        }
        return result;
    }

    void ConfigureIssueGrid()
    {
        issues.AutoGenerateColumns=false;issues.VirtualMode=true;issues.ReadOnly=false;
        issues.Columns.Add(new DataGridViewCheckBoxColumn{Name="选择",HeaderText="选择",Width=56,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        foreach(string name in new[]{"索引","检测项目","元件类型","元件位号","源行","标准值","盲点原因","问题"})issues.Columns.Add(new DataGridViewTextBoxColumn{Name=name,HeaderText=name,ReadOnly=true,Visible=name!="索引",SortMode=DataGridViewColumnSortMode.NotSortable});
        issues.CellValueNeeded+=(_,e)=>
        {
            if(e.RowIndex<0||e.RowIndex>=issueTargets.Count)return;
            var t=issueTargets[e.RowIndex];e.Value=e.ColumnIndex switch{0=>checkedIssues.Contains(e.RowIndex),1=>e.RowIndex,2=>t.Target.Inspection.Name(),3=>t.Item.Type,4=>t.Item.Reference,5=>t.Item.SourceRow,6=>t.Item.Value,7=>cfg.ReasonText(t.Item.OutputReasonCode),_=>string.Join("；",t.Item.Issues.Select(i=>i.Message))};
        };
        issues.CellValuePushed+=(_,e)=>{if(e.ColumnIndex!=0||!CanSelectIssue(e.RowIndex))return;if(e.Value is true)checkedIssues.Add(e.RowIndex);else checkedIssues.Remove(e.RowIndex);};
        issues.CellBeginEdit+=(_,e)=>{if(e.ColumnIndex==0&&!CanSelectIssue(e.RowIndex))e.Cancel=true;};
        issues.CellFormatting+=(_,e)=>{if(e.RowIndex>=0&&!CanSelectIssue(e.RowIndex)&&e.CellStyle!=null)e.CellStyle.ForeColor=AppTheme.Muted;};
    }

    bool CanSelectIssue(int index)=>index>=0&&index<issueTargets.Count&&issueTargets[index].Item.SourceState==TestState.Untestable&&!issueTargets[index].Item.HasDataIssues;

    void SelectAllIssues()
    {
        checkedIssues.Clear();if(selectAllIssues.Checked)for(int i=0;i<issueTargets.Count;i++)if(CanSelectIssue(i))checkedIssues.Add(i);
        issues.Invalidate();
    }

    void ConfigureMainGrid()
    {
        filterDelay.Tick+=(_,_)=>{filterDelay.Stop();RefreshResults();};
        FormClosed+=(_,_)=>filterDelay.Dispose();
    }

    void ScheduleFilter(){filterDelay.Stop();filterDelay.Start();}

    void DisplayMainRows(List<DisplayRow> rows)
    {
        grid.CancelPendingScroll();
        mainRows=rows.Select(r=>r.Cells).ToArray();
        grid.SetRows(mainRows);
    }

    void InvalidateDetailPages()
    {
        dirtyPages.UnionWith(new[]{"复制","异常待确认","人工修改日志"});
        if(report!=null)
        {
            var filter=CurrentFilter();int count=report.Analyses.Where(p=>!selectedKind.HasValue||p.Key==selectedKind).Sum(p=>AnalysisEngine.Filter(p.Value,filter,cfg).Count(c=>c.Pending));
            nav["异常待确认"].Text="异常待确认"+(count>0?" "+count:"");
        }
        RefreshVisiblePage();
    }

    void RefreshVisiblePage()
    {
        if(!dirtyPages.Remove(currentPage))return;
        switch(currentPage)
        {
            case "复制":RefreshCopy();break;
            case "异常待确认":RefreshIssues([]);break;
            case "人工修改日志":RefreshLog();break;
            case "规则设置":case "规则总览":RefreshRules();break;
            case "盲点设置":RefreshReasons();break;
            case "类型分类设置":RefreshTypes();break;
        }
    }

    string SessionDirectory=>Path.Combine(userDir,"sessions-v6");
    bool closingAfterSave;
    async void HandleClosing(object? sender,FormClosingEventArgs e)
    {
        if(closingAfterSave)return;
        if(busy){e.Cancel=true;return;}
        if(report==null||!report.Analyses.Values.Any(a=>a.Edits.Count>0||a.Corrections.Count>0))return;
        e.Cancel=true;bool saved=false;
        await RunBusy(async()=>{status.Text="正在保存人工记录…";await SaveSessionAsync();saved=true;});
        if(saved){closingAfterSave=true;Close();}
    }

    async Task SaveSessionAsync()
    {
        var current=report;if(current==null||!current.Analyses.Values.Any(a=>a.Edits.Count>0||a.Corrections.Count>0))return;
        // Hashing rules and serializing session history must not block the window message loop.
        await Task.Run(()=>BatchReasonService.Save(current,SessionPath()));
    }

    async Task LoadSessionAsync(CoverageReport incoming)
    {
        string? path=await Task.Run(()=>Directory.GetFiles(SessionDirectory,incoming.SourceFingerprint+"-*.session.json").Concat(Directory.GetFiles(userDir,incoming.SourceFingerprint+"-*.session.json")).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault());
        if(path==null||MessageBox.Show(this,"恢复该报告的最终人工原因记录？","恢复记录",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        status.Text="正在恢复人工原因记录…";
        try
        {
            await Task.Run(()=>
            {
                string json=File.ReadAllText(path);using var doc=System.Text.Json.JsonDocument.Parse(json);
                CoverageReport saved;
                if(doc.RootElement.TryGetProperty("Analyses",out _)||doc.RootElement.TryGetProperty("analyses",out _))saved=System.Text.Json.JsonSerializer.Deserialize<CoverageReport>(json,RuleConfig.JsonOptions)!;
                else{var a=System.Text.Json.JsonSerializer.Deserialize<Analysis>(json,RuleConfig.JsonOptions)!;saved=new(){SourceFingerprint=a.SourceFingerprint,Analyses={{InspectionKind.Missing,a}}};}
                ReportWorkflow.Restore(incoming,saved,cfg);
            });
        }
        catch(Exception ex){MessageBox.Show(this,"记录未能恢复："+ex.Message);}
    }
}
