# Replace hard-coded hex colors in XAML files with StaticResource brushes defined in Themes/Colors.xaml
# Usage: run from repository root

$map = @{
    '#F9FAFB' = '{StaticResource BackgroundBrush}'
    '#1F2937' = '{StaticResource HeaderBackgroundBrush}'
    '#10B981' = '{StaticResource SuccessBrush}'
    '#059669' = '{StaticResource ButtonHoverBrush}'
    '#2563EB' = '{StaticResource PrimaryBrush}'
    '#3B82F6' = '{StaticResource AccentBrush}'
    '#EF4444' = '{StaticResource DangerBrush}'
    '#E5E7EB' = '{StaticResource BorderBrushValue}'
    '#6B7280' = '{StaticResource MutedBrush}'
    '#111827' = '{StaticResource TextPrimaryBrush}'
    '#F59E0B' = '{StaticResource WarningBrush}'
    '#DBEAFE' = '{StaticResource InfoBackgroundBrush}'
}

# Attribute names to check for duplicates (will remove hex-based duplicate if a resource-based attribute exists)
$attrs = @('Background','Foreground','BorderBrush','Fill','RowBackground','AlternatingRowBackground')

Write-Output "Searching .xaml files..."
$files = Get-ChildItem -Recurse -Include *.xaml | Where-Object { $_.FullName -notmatch 'Themes\\Colors.xaml' }
$changed = @()

foreach ($f in $files) {
    $text = Get-Content -Raw -LiteralPath $f.FullName
    $original = $text

    # Replace hex codes with resource tokens
    foreach ($k in $map.Keys) {
        # Replace both uppercase and lowercase occurrences
        $text = $text -replace [regex]::Escape($k), $map[$k]
        $text = $text -replace [regex]::Escape($k.ToLower()), $map[$k]
    }

    # Remove duplicated attributes in the same start tag: if same attribute appears more than once and one uses StaticResource, remove the other one if it contains a '{StaticResource' or a hex code
    $text = [regex]::Replace($text, '<([\w:\-]+)([^>]*)>', {
        param($m)
        $tag = $m.Groups[1].Value
        $rest = $m.Groups[2].Value
        $newRest = $rest
        foreach ($attr in $attrs) {
            # find all occurrences of attr="..." within $rest
            $pattern = "$attr\s*=\s*\"([^\"]*)\""
            $matches = [regex]::Matches($rest, $pattern)
            if ($matches.Count -gt 1) {
                # prefer keeping the one that contains StaticResource; otherwise keep the last occurrence
                $keepIndex = -1
                for ($i=0; $i -lt $matches.Count; $i++) {
                    if ($matches[$i].Groups[1].Value -match '\{StaticResource') { $keepIndex = $i; break }
                }
                if ($keepIndex -eq -1) { $keepIndex = $matches.Count - 1 }

                # remove other occurrences
                for ($i=0; $i -lt $matches.Count; $i++) {
                    if ($i -ne $keepIndex) {
                        $toRemove = [regex]::Escape($matches[$i].Value)
                        $newRest = [regex]::Replace($newRest, $toRemove, '', 1)
                    }
                }
            }
        }
        return "<${tag}${newRest}>"
    }, 'Singleline')

    if ($text -ne $original) {
        Set-Content -LiteralPath $f.FullName -Value $text -Encoding UTF8
        $changed += $f.FullName
        Write-Output "Patched: $($f.FullName)"
    }
}

Write-Output "Total files changed: $($changed.Count)"
if ($changed.Count -gt 0) { $changed | Out-File -FilePath scripts/changed-xaml-files.txt -Encoding utf8 }

Write-Output "Done."
