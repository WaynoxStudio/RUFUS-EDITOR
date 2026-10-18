using System.IO.Compression;
var swfPath = @"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient\clips\maps\0.swf";
var file = File.ReadAllBytes(swfPath);
using var zs = new ZLibStream(new MemoryStream(file, 8, file.Length - 8), CompressionMode.Decompress);
using var ms = new MemoryStream(); zs.CopyTo(ms); var raw = ms.ToArray();
var nBits = raw[0] >> 3; var pos = (5 + nBits * 4 + 7) / 8 + 4;
while (pos < raw.Length-1) {
  var cl=BitConverter.ToUInt16(raw,pos); pos+=2; var t=cl>>6; var len=cl&0x3F;
  if(len==0x3F){len=BitConverter.ToInt32(raw,pos); pos+=4;}
  if (t==36 && BitConverter.ToUInt16(raw,pos)==223) {
    var w=BitConverter.ToUInt16(raw,pos+3); var h=BitConverter.ToUInt16(raw,pos+5);
    using var z=new ZLibStream(new MemoryStream(raw,pos+7,len-7), CompressionMode.Decompress);
    using var o=new MemoryStream(); z.CopyTo(o); var argb=o.ToArray();
    int whiteEdge=0, opaque=0;
    for(int y=0;y<h;y++) for(int x=0;x<w;x++){
      var i=y*w+x; var a=argb[i*4]; if(a==0) continue; opaque++;
      var r=argb[i*4+1]; var g=argb[i*4+2]; var b=argb[i*4+3];
      bool nearClear=false;
      for(int dy=-1;dy<=1&&!nearClear;dy++) for(int dx=-1;dx<=1&&!nearClear;dx++){
        int nx=x+dx,ny=y+dy; if((uint)nx>=(uint)w||(uint)ny>=(uint)h){nearClear=true;continue;}
        if(argb[(ny*w+nx)*4]==0) nearClear=true;
      }
      var warm=r-b; var lum=(r*3+g*4+b)/8;
      if(nearClear && lum>=210 && warm<28) whiteEdge++;
    }
    Console.WriteLine($"opaque={opaque} whiteHaloEdge={whiteEdge}");
  }
  pos+=len; if(t==0) break;
}
