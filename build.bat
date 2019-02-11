FOR /F %%I IN ("%0") DO SET CURRENTDIR=%%~dpI
SET SOLUTIONDIR=%CURRENTDIR%

SET PROJECTPATH=%SOLUTIONDIR%RefOwner\RefOwner.csproj
SET DOTNETFX=v4.6.1

REM ================== .NET 4.0 ======================

:Net40x86Release
REM x86 / Release
SET OUTPUTPATH=%SOLUTIONDIR%\bin
msbuild "%PROJECTPATH%" /t:rebuild /p:Configuration=Release;AssemblyName=RefOwner32 /p:PlatformTarget=x86;Platform=x86;TargetFrameworkVersion=%DOTNETFX% /p:OutputPath=%OUTPUTPATH%

:Net40x64Release
REM x64 / Release
SET OUTPUTPATH=%SOLUTIONDIR%\bin
msbuild "%PROJECTPATH%" /t:rebuild /p:Configuration=Release;AssemblyName=RefOwner64 /p:PlatformTarget=x64;Platform=x64;TargetFrameworkVersion=%DOTNETFX% /p:OutputPath=%OUTPUTPATH%

:EndOfBuild