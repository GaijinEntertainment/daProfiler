Echo Building shaders
Echo Launch dir: "%~dp0"
Echo Current dir: "%CD%"
cd "%~dp0"
PATH="%GDEVTOOL%\win.sdk.100\bin\10.0.18362.0\x64\"
fxc.exe /T vs_4_0 /O3 /E VS /Fo Basic_vs.fxo Basic.fx
fxc.exe /T ps_4_0 /O3 /E PS /Fo Basic_ps.fxo Basic.fx
fxc.exe /T vs_4_0 /O3 /E VS /Fo Text_vs.fxo Text.fx
fxc.exe /T ps_4_0 /O3 /E PS /Fo Text_ps.fxo Text.fx
