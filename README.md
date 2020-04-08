# VL.IO.NDI

Repository containing:

* src : Visual Studio Project with a .net wrapper for native NDI libs (tested with version 4.x)
  * Based on NDILibDotNet2 wrapper example from [NDI SDK](https://www.ndi.tv/sdk/) and adapted for use with VL
* VL.IO.NDI: VL project, containing nodes to receive NDI streams



### Setup:

* Install latest [NDI Runtime](http://new.tk/NDIRedistV4) - it contains the latest NDI dlls.
  * copy *Processing.NDI.Lib.x64.dll* to *VL.IO.NDI\lib-native\x64* 
* Build the solution in *NDILibDotNet2_VL*
  * copy *NDILibDotNet2_VL.dll* from *src\bin\Debug\netstandard2.0* to *VL.IO.NDI\lib\netstandard2.0*



### Recommended:

Download the [NDI Tools](https://www.ndi.tv/tools/). They contain several applications (like *Test Patterns* and *Scan Converter*) to create NDI Sources on your computer.



