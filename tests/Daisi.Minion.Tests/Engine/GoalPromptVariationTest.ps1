$MINION = "C:\repos\daisinet-overlord-summoner-minions\daisi-minion\src\Daisi.Minion\bin\Release\net10.0\daisi-minion.exe"
$MODEL = "C:\GGUFS\Qwen3.5-9B-Q4_K_M.gguf"

# Reset test file before each run
function Reset-TestFile {
    Set-Location "C:\minion-dev"
    "original content here" | Set-Content "test-variation.txt"
}

# Run a single test and check if the tool was called
function Test-Variation {
    param([string]$Name, [string]$Goal, [int]$MaxTokens = 1024, [int]$Iters = 2)

    Reset-TestFile

    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
    $output = & $MINION --cli --type code --model $MODEL --context 4096 --max-tokens $MaxTokens --max-iterations $Iters --goal $Goal 2>&1 | Out-String

    $toolCalled = $output -match '\[file_edit\]|\[file_write\]|\[shell\]|\[file_read\]'
    $completed = $output -match 'Goal completed'
    $content = Get-Content "C:\minion-dev\test-variation.txt" -Raw
    $edited = $content -ne "original content here`r`n"

    Write-Host "  Tool called: $toolCalled | Completed: $completed | File edited: $edited"

    return @{Name=$Name; ToolCalled=$toolCalled; Completed=$completed; Edited=$edited}
}

$results = @()

# Variation 1: Current behavior (baseline)
$results += Test-Variation "Baseline" "Use file_edit on test-variation.txt to replace 'original content here' with 'MODIFIED by minion'"

# Variation 2: Ultra-direct, single sentence
$results += Test-Variation "Ultra-direct" "Call file_edit now: path=test-variation.txt, old_string='original content here', new_string='MODIFIED by minion'"

# Variation 3: Lower max tokens (force shorter thinking)
$results += Test-Variation "Low-tokens" "Use file_edit on test-variation.txt to replace 'original content here' with 'MODIFIED by minion'" -MaxTokens 256

# Variation 4: Very low tokens
$results += Test-Variation "Very-low-tokens" "Use file_edit on test-variation.txt to replace 'original content here' with 'MODIFIED by minion'" -MaxTokens 128

# Variation 5: Shell instead of file_edit
$results += Test-Variation "Shell-approach" "Run shell: powershell -Command `"(Get-Content test-variation.txt) -replace 'original content here','MODIFIED by minion' | Set-Content test-variation.txt`""

# Variation 6: file_write instead of file_edit
$results += Test-Variation "File-write" "Use file_write to create test-variation.txt with content 'MODIFIED by minion'"

# Variation 7: Multi-step with explicit numbering
$results += Test-Variation "Numbered-steps" "Step 1: Call file_read with path='test-variation.txt'. Step 2: Call file_edit with path='test-variation.txt', old_string='original content here', new_string='MODIFIED by minion'."

# Variation 8: More iterations
$results += Test-Variation "More-iters" "Use file_edit on test-variation.txt to replace 'original content here' with 'MODIFIED by minion'" -Iters 5

Write-Host "`n=== RESULTS ===" -ForegroundColor Green
$results | ForEach-Object {
    $status = if ($_.Edited) { "PASS" } else { "FAIL" }
    Write-Host "  [$status] $($_.Name): tool=$($_.ToolCalled) complete=$($_.Completed) edited=$($_.Edited)"
}

# Cleanup
Remove-Item "C:\minion-dev\test-variation.txt" -ErrorAction SilentlyContinue
