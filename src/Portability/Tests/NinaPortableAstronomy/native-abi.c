#include <stddef.h>
#include <stdio.h>
#include "novas.h"
#include "sofa.h"
#define SIZE(t, m) printf("%s=%zu\n", #m, sizeof(t))
#define FIELD(t, f, m) printf("%s=%zu\n", #m, offsetof(t,f))
int main(void) {
 _Static_assert(sizeof(short) == 2 && sizeof(int) == 4 && sizeof(double) == 8, "Unexpected primitive ABI");
 _Static_assert(sizeof(((cat_entry *)0)->starnumber) == 4, "Catalogue number must match managed Int32");
 SIZE(iauASTROM, Astrom); SIZE(cat_entry, CatalogueEntry); SIZE(object, CelestialObject);
 SIZE(on_surface, OnSurface); SIZE(in_space, InSpace); SIZE(observer, Observer); SIZE(sky_pos, SkyPosition);
 FIELD(cat_entry,starname,CatalogueEntry.StarName); FIELD(cat_entry,catalog,CatalogueEntry.Catalog); FIELD(cat_entry,starnumber,CatalogueEntry.StarNumber);
 FIELD(cat_entry,ra,CatalogueEntry.RA); FIELD(cat_entry,dec,CatalogueEntry.Dec); FIELD(cat_entry,promora,CatalogueEntry.ProMoRA); FIELD(cat_entry,promodec,CatalogueEntry.ProMoDec); FIELD(cat_entry,parallax,CatalogueEntry.Parallax); FIELD(cat_entry,radialvelocity,CatalogueEntry.RadialVelocity);
 FIELD(object,type,CelestialObject.Type); FIELD(object,number,CelestialObject.Number); FIELD(object,name,CelestialObject.Name); FIELD(object,star,CelestialObject.Star);
 FIELD(on_surface,latitude,OnSurface.Latitude); FIELD(on_surface,longitude,OnSurface.Longitude); FIELD(on_surface,height,OnSurface.Height); FIELD(on_surface,temperature,OnSurface.Temperature); FIELD(on_surface,pressure,OnSurface.Pressure);
 FIELD(in_space,sc_pos,InSpace.ScPos); FIELD(in_space,sc_vel,InSpace.ScVel);
 FIELD(observer,where,Observer.Where); FIELD(observer,on_surf,Observer.OnSurf); FIELD(observer,near_earth,Observer.NearEarth);
 FIELD(sky_pos,r_hat,SkyPosition.RHat); FIELD(sky_pos,ra,SkyPosition.RA); FIELD(sky_pos,dec,SkyPosition.Dec); FIELD(sky_pos,dis,SkyPosition.Dis); FIELD(sky_pos,rv,SkyPosition.RV);
 FIELD(iauASTROM,pmt,Astrom.pmt); FIELD(iauASTROM,eb[0],Astrom.eb0); FIELD(iauASTROM,eb[1],Astrom.eb1); FIELD(iauASTROM,eb[2],Astrom.eb2);
 FIELD(iauASTROM,eh[0],Astrom.eh0); FIELD(iauASTROM,eh[1],Astrom.eh1); FIELD(iauASTROM,eh[2],Astrom.eh2); FIELD(iauASTROM,em,Astrom.em);
 FIELD(iauASTROM,v[0],Astrom.v0); FIELD(iauASTROM,v[1],Astrom.v1); FIELD(iauASTROM,v[2],Astrom.v2); FIELD(iauASTROM,bm1,Astrom.bm1);
 FIELD(iauASTROM,bpn[0][0],Astrom.bpn00); FIELD(iauASTROM,bpn[0][1],Astrom.bpn01); FIELD(iauASTROM,bpn[0][2],Astrom.bpn02);
 FIELD(iauASTROM,bpn[1][0],Astrom.bpn10); FIELD(iauASTROM,bpn[1][1],Astrom.bpn11); FIELD(iauASTROM,bpn[1][2],Astrom.bpn12);
 FIELD(iauASTROM,bpn[2][0],Astrom.bpn20); FIELD(iauASTROM,bpn[2][1],Astrom.bpn21); FIELD(iauASTROM,bpn[2][2],Astrom.bpn22);
 FIELD(iauASTROM,along,Astrom.along); FIELD(iauASTROM,phi,Astrom.phi); FIELD(iauASTROM,xpl,Astrom.xpl); FIELD(iauASTROM,ypl,Astrom.ypl);
 FIELD(iauASTROM,sphi,Astrom.sphi); FIELD(iauASTROM,cphi,Astrom.cphi); FIELD(iauASTROM,diurab,Astrom.diurab); FIELD(iauASTROM,eral,Astrom.eral); FIELD(iauASTROM,refa,Astrom.refa); FIELD(iauASTROM,refb,Astrom.refb);
 return 0;
}
