param([string]$Executable)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if (!$Executable) {
    $taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $Executable = Join-Path $env:ProgramFiles "RobloxLaunchHelper\$taskSid\RobloxLaunchHelper.exe"
}
$taskReportDirectory = Join-Path $PSScriptRoot '..\.build\user-simulation'
New-Item -ItemType Directory -Force -Path $taskReportDirectory | Out-Null
$taskResults = [Collections.Generic.List[object]]::new()
$taskApp = $null
$taskRoot = $null
$taskOriginalScheme = $null
$taskStage = 'Open workspace'
function Wait-For([scriptblock]$Condition) {
    $taskClock = [Diagnostics.Stopwatch]::StartNew()
    while ($taskClock.Elapsed.TotalSeconds -lt 15) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'UI state did not become ready within 15 seconds.'
}
function Find-Element([string]$Name) {
    $taskCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name)
    $taskRoot.FindFirst([Windows.Automation.TreeScope]::Descendants, $taskCondition)
}
function Invoke-Button([string]$Name) {
    $taskElement = Find-Element $Name
    if (!$taskElement) { throw "Missing control: $Name" }
    $taskPattern = $taskElement.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $taskPattern.Invoke()
}
function Open-Workspace {
    $script:taskApp = Start-Process -FilePath $Executable -PassThru -WindowStyle Normal
    Wait-For { $taskApp.Refresh(); $taskApp.MainWindowHandle -ne 0 }
    $script:taskRoot = [Windows.Automation.AutomationElement]::FromHandle($taskApp.MainWindowHandle)
    Wait-For { $null -ne (Find-Element 'Navigate to Instances') }
}
function Find-Combo {
    $taskCondition=[Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Color scheme'),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ComboBox))
    $taskRoot.FindFirst([Windows.Automation.TreeScope]::Descendants,$taskCondition)
}
function Select-Scheme([string]$Name) {
    $taskCombo=Find-Combo
    if(!$taskCombo) { throw 'Appearance combo is unavailable.' }
    $taskCombo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $taskCondition=[Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]@(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$Name),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$taskApp.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
    Wait-For { $null -ne [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,$taskCondition) }
    $taskChoice=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,$taskCondition)
    $taskPattern=$null
    if($taskChoice.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$taskPattern)) { $taskPattern.Select() }
    else { $taskChoice.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    try { $taskCombo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
}
function Current-Scheme {
    $taskCombo=Find-Combo
    if(!$taskCombo) { throw 'Appearance combo is unavailable.' }
    $taskCombo.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Visible-AppWindows {
    $taskCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$taskApp.Id)
    @([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$taskCondition) |
        Where-Object { !$_.Current.IsOffscreen }).Count
}
try {
    Open-Workspace
    $taskRoot.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize(1050,760)
    foreach ($taskPage in @('Instances','Layouts','Activity','System','Settings')) {
        $taskStage = "Navigate $taskPage"
        Invoke-Button "Navigate to $taskPage"
        Wait-For { $taskHeading=Find-Element $taskPage; $taskHeading -and !$taskHeading.Current.IsOffscreen }
        if ($taskPage -eq 'Layouts') { Wait-For { $taskPreview = Find-Element 'Preview'; $taskPreview -and $taskPreview.Current.IsEnabled } }
        if ((Visible-AppWindows) -ne 1) { throw 'Navigation created an additional visible application window.' }
        $taskResults.Add(@{ Check="Navigate $taskPage"; Status='passed' })
    }
    $taskOriginalScheme = Current-Scheme
    $taskStage = 'Select Light appearance'
    Select-Scheme 'Light'
    Wait-For { (Current-Scheme) -eq 'Light' }
    $taskApp.CloseMainWindow() | Out-Null
    if (!$taskApp.WaitForExit(15000)) { throw 'Workspace did not close gracefully.' }
    Open-Workspace
    $taskStage = 'Verify persisted appearance'
    Invoke-Button 'Navigate to Settings'
    Wait-For { $taskPicker=Find-Combo; $taskPicker -and !$taskPicker.Current.IsOffscreen }
    if ((Current-Scheme) -ne 'Light') { throw 'Appearance did not persist after reopening.' }
    Select-Scheme $taskOriginalScheme
    $taskStage = 'Route update request'
    $taskResults.Add(@{ Check='Appearance persists across app restart and original choice restored'; Status='passed' })
    $taskUpdateRequest = Start-Process -FilePath $Executable -ArgumentList '--updates' -PassThru -WindowStyle Hidden
    if (!$taskUpdateRequest.WaitForExit(15000) -or $taskUpdateRequest.ExitCode -ne 0) { throw 'Update navigation request did not finish successfully.' }
    Wait-For { $taskCheck = Find-Element 'Check for updates'; $taskCheck -and !$taskCheck.Current.IsOffscreen }
    if ((Visible-AppWindows) -ne 1) { throw 'Update navigation opened an additional visible application window.' }
    $taskResults.Add(@{ Check='Updates command routes into existing workspace'; Status='passed' })
}
catch { $taskResults.Add(@{ Check='Installed workspace smoke'; Status='failed'; Stage=$taskStage; Reason=$_.Exception.Message; Location=$_.ScriptStackTrace }); throw }
finally {
    if ($taskApp -and !$taskApp.HasExited) {
        if ($taskOriginalScheme) { try { Invoke-Button 'Navigate to Settings'; Select-Scheme $taskOriginalScheme } catch { } }
        $taskApp.CloseMainWindow() | Out-Null
        $null = $taskApp.WaitForExit(15000)
    }
    $taskResults | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskReportDirectory 'installed-workspace-smoke.json')
    $taskResults | Format-Table -AutoSize
}
