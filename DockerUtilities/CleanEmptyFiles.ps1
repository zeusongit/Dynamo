<#
   Date: 07/15/2019
   Purpose: Clean empty files
#>

# Dynamo's location
$DynamoRoot = "C:\Jenkins\workspace\Dynamo\Dynamo\DYN-1822"

$Xpath = "//test-case"

Get-ChildItem "$DynamoRoot\TestResults" -Filter *.xml | 
Foreach-Object {
    $Path = $_.FullName
    [xml]$Types = Get-Content $Path
    $node = Select-Xml -Xml $Types -XPath $Xpath
    If (!$node.Node) {
        Remove-Item -LiteralPath $Path -ErrorAction Ignore
    }
}
