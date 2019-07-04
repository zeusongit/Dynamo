<#
   Date: 07/04/2019
   Purpose: Run commands on parallel
#>

workflow _Wkf_StartCommands {

$ScriptToRun= ".\ParallelTest.ps1"

$pathFast = "C:\\Jenkins\\workspace\\Dynamo\\Dynamo\\DYN-1822\\src\\Tools\\TransformXMLToCSVTool\\Result\\textFileWithFiltersFastTests.txt"

$pathSlow = "C:\\Jenkins\\workspace\\Dynamo\\Dynamo\\DYN-1822\\src\\Tools\\TransformXMLToCSVTool\\Result\\textFileWithFiltersSlowTests.txt"

    parallel {


        #{&$ScriptToRun -path $pathFast}
        #{&$ScriptToRun -path $pathFast}

        #{&".\ParallelTest.ps1 -path " + $pathFast}
        #{&".\ParallelTest.ps1 -path " + $pathFast}


        #InlineScript { '$ScriptToRun -path $pathFast' }
        #InlineScript { '$ScriptToRun -path $pathFast' }


        Start-Process -FilePath Powershell.exe -ArgumentList $ScriptToRun + " -path " + $pathFast
        Start-Process -FilePath Powershell.exe -ArgumentList $ScriptToRun + " -path " + $pathFast
        
    }

}

_Wkf_StartCommands