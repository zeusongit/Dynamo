<#
   Date: 07/04/2019
   Purpose: Parallel test
#>
$ErrorActionPreference = "Stop"

$NUnitDir = "C:\Program Files (x86)\NUnit.org\nunit-console\"
Set-Location -Path $NUnitDir

New-Item -Path "C:\Jenkins\workspace\Dynamo\Dynamo\DYN-1822" -Name "TestResults" -ItemType "directory"

workflow RunTests_Parallel {
    param($Tests, $RunInParallel)

    # Dynamo's location
    $DynamoRoot = "C:\Jenkins\workspace\Dynamo\Dynamo\DYN-1822"
    
    # Location of the .dll's
    $ProjectDir = "$DynamoRoot\bin\AnyCPU\Release"

    # Location of the NUnit Console
    $NunitTool = "nunit3-console.exe"

    If ($RunInParallel -eq 'true') {
        foreach -parallel ($Test in $Tests){
            $AssemblyLocation = $ProjectDir + '\' + $Test.TestAssembly

            $ParallelExecutionArguments = $AssemblyLocation + ' --where="test=="' + $Test.TestNamespace + '" and cat != Failure and cat != BackwardIncompatible" --labels=Before --result="' + $DynamoRoot + '\TestResults\TestResult-' + $Test.TestClass + '.xml";format=nunit2'

            #Start an NUnit console instance for the current test
            Start-Process -FilePath $NunitTool -ArgumentList $ParallelExecutionArguments -Wait
        }
    }  Else {
        foreach ($Test in $Tests){
            $AssemblyLocation = $ProjectDir + '\' + $Test.TestAssembly

            $ParallelExecutionArguments = $AssemblyLocation + ' --where="test=="' + $Test.TestNamespace + '" and cat != Failure and cat != BackwardIncompatible" --labels=Before --result="' + $DynamoRoot + '\TestResults\TestResult-' + $Test.TestClass + '.xml";format=nunit2'

            #Start an NUnit console instance for the current test
            Start-Process -FilePath $NunitTool -ArgumentList $ParallelExecutionArguments -Wait
        }
    }
}

workflow _Wkf_StartCommands {

    $SlowPath = "C:\\Jenkins\\workspace\\Dynamo\\Dynamo\\DYN-1822\\src\\Tools\\TransformXMLToCSVTool\\Result\\textFileWithFiltersSlowTests.txt" 
    $FastPath = "C:\\Jenkins\\workspace\\Dynamo\\Dynamo\\DYN-1822\\src\\Tools\\TransformXMLToCSVTool\\Result\\textFileWithFiltersFastTests.txt"
    $NonParallelPath = "C:\\Jenkins\\workspace\\Dynamo\\Dynamo\\DYN-1822\\src\\Tools\\TransformXMLToCSVTool\\Result\\textFileWithFiltersNoParallelSlowTests.txt"
    
    # Map the .csv file into an object we can work with
    $SlowTests = Import-Csv $SlowPath -Header 'TestClass', 'TestNamespace', 'TestAssembly'
    $FastTests = Import-Csv $FastPath -Header 'TestClass', 'TestNamespace', 'TestAssembly'
    $NonParallelTests = Import-Csv $NonParallelPath -Header 'TestClass', 'TestNamespace', 'TestAssembly'
    
    parallel {
        RunTests_Parallel -Tests $FastTests -RunInParallel true
        RunTests_Parallel -Tests $SlowTests -RunInParallel true
        RunTests_Parallel -Tests $NonParallelTests -RunInParallel false
    }
}

$StopWatch = [System.Diagnostics.Stopwatch]::StartNew()
#Wait a bit
_Wkf_StartCommands
$StopWatch.Stop()
$StopWatch.Elapsed