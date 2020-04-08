# VL.IO.NDI

Repository containing:

* *src\\\** : Visual Studio Project with a .net wrapper for native NDI libs (tested with version 4.x)
  * Based on NDILibDotNet2 wrapper example from [NDI SDK](https://www.ndi.tv/sdk/) and adapted for use with VL
* *VL.IO.NDI.vl*: VL-nodes to receive NDI streams



### Setup:

* Install latest [NDI Runtime](http://new.tk/NDIRedistV4) - it contains the latest NDI dlls.
  * copy *Processing.NDI.Lib.x64.dll* to *VL.IO.NDI\lib-native\x64* 
* Build the VS solution in *src\\*



### Recommended:

Download the [NDI Tools](https://www.ndi.tv/tools/). They contain several applications (like *Test Patterns* and *Scan Converter*) to create NDI Sources on your computer.



