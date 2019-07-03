param($path)

# Map the .csv file into an object we can work with
$Tests = Import-Csv $path -Header 'TestClass', 'TestNamespace', 'TestAssembly'

$NUnitDir = "C:\Program Files (x86)\NUnit.org\nunit-console\"
Set-Location -Path $NUnitDir

New-Item -Path "C:\Jenkins\workspace\Dynamo\Dynamo\DYN-1822" -Name "TestResults" -ItemType "directory"

$Is64 = [System.Environment]::Is64BitProcess
$Is64

workflow RunTests_Parallel {
    param($Tests)

    # Dynamo's location
    $DynamoRoot = "C:\Jenkins\workspace\Dynamo\Dynamo\DYN-1822"

    # Location of the .dll's
    $ProjectDir = "$DynamoRoot\bin\AnyCPU\Release"

    # Location of the NUnit Console
    $NunitTool = "nunit3-console.exe"

    foreach -parallel ($Test in $Tests){
        $AssemblyLocation = $ProjectDir + '\' + $Test.TestAssembly

        #--trace==Verbose --dispose-runners
        #$ParallelExecutionArguments = $AssemblyLocation + ' --where="test=="' + $Test.TestNamespace + '" and cat != Failure and cat != BackwardIncompatible" --labels=Before --result="' + $DynamoRoot + '\TestResults\TestResult-' + $Test.TestClass + '.xml";format=nunit2'
        $ParallelExecutionArguments = $AssemblyLocation + ' --where="test=="' + $Test.TestNamespace + '" and cat != Failure and cat != BackwardIncompatible" --labels=Before --result="' + $DynamoRoot + '\TestResults\TestResult-' + $Test.TestClass + '.xml";format=nunit2'

        #Start an NUnit console instance for the current test
        Start-Process -FilePath $NunitTool -ArgumentList $ParallelExecutionArguments -Wait
    }
}

$StopWatch = [System.Diagnostics.Stopwatch]::StartNew()
#Wait a bit
RunTests_Parallel -Tests $Tests
$StopWatch.Stop()
$StopWatch.Elapsed