param([string]$FilePath)

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
try {
    $wb = $excel.Workbooks.Open($FilePath)
    $ws = $wb.Sheets.Item(1)
    
    $maxRow = $ws.UsedRange.Rows.Count
    $maxCol = $ws.UsedRange.Columns.Count

    for($r=1; $r -le $maxRow; $r++) {
        $rowStr = @()
        for($c=1; $c -le $maxCol; $c++) {
            $cell = $ws.Cells.Item($r, $c)
            $rowStr += "`"$($cell.Text)`""
        }
        Write-Output ($rowStr -join "`t")
    }
} finally {
    if ($wb) { $wb.Close($false) }
    $excel.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
}
