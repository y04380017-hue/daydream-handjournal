using UnityEngine;
namespace VintageJournal
{
    // All rotations use physical page units, not a distorted screen/normalized square.
    public static class DecorationGeometry
    {
        public static Vector2 Extents(DecorationDefinition def,JournalDecorationData d,Vector2 page)
        {
            if(d.physicalMaterial&&d.physicalPageSize.x>0&&d.physicalPageSize.y>0)page=d.physicalPageSize;
            Vector2 n=DecorationRenderer.EffectiveSize(def,d);float c=Mathf.Abs(Mathf.Cos(d.rotationDegrees*Mathf.Deg2Rad)),s=Mathf.Abs(Mathf.Sin(d.rotationDegrees*Mathf.Deg2Rad));
            return new Vector2((n.x*page.x*c+n.y*page.y*s)/page.x,(n.x*page.x*s+n.y*page.y*c)/page.y)*.5f;
        }
        public static void Constrain(DecorationDefinition def,JournalDecorationData d,Vector2 page)
        {
            if(d.physicalMaterial)
            {
                if(d.physicalPageSize.x>0&&d.physicalPageSize.y>0)page=d.physicalPageSize;
                d.scale=Vector2.one*Mathf.Clamp(d.scale.x,.1f,3f);d.length=Mathf.Clamp(d.length,.3f,2.5f);
                d.rotationDegrees=Mathf.Repeat(d.rotationDegrees+180,360)-180;
                var physicalExtents=Extents(def,d,page);
                if(physicalExtents.x<.5f&&physicalExtents.y<.5f)
                    d.position=new Vector2(Mathf.Clamp(d.position.x,physicalExtents.x,1-physicalExtents.x),Mathf.Clamp(d.position.y,physicalExtents.y,1-physicalExtents.y));
                return;
            }
            if(d.size.x<=0||d.size.y<=0)d.size=def.defaultSize;
            d.length=Mathf.Clamp(d.length,.3f,2.5f);float scale=Mathf.Clamp(d.scale.x,def.minScale,def.maxScale);d.scale=Vector2.one*scale;
            d.rotationDegrees=Mathf.Repeat(d.rotationDegrees+180,360)-180;
            var e=Extents(def,d,page);float fit=Mathf.Min(1,.485f/Mathf.Max(.001f,e.x),.485f/Mathf.Max(.001f,e.y));
            if(fit<1)
            {
                float fitted=d.scale.x*fit;
                if(fitted<def.minScale){d.size*=fitted/def.minScale;d.scale=Vector2.one*def.minScale;}
                else d.scale=Vector2.one*fitted;
                e=Extents(def,d,page);
            }
            d.position=new Vector2(Mathf.Clamp(d.position.x,e.x+.006f,1-e.x-.006f),Mathf.Clamp(d.position.y,e.y+.006f,1-e.y-.006f));
        }
        public static Vector2 Offset(DecorationDefinition def,JournalDecorationData d,Vector2 corner,Vector2 page)
        {
            if(d.physicalMaterial&&d.physicalPageSize.x>0&&d.physicalPageSize.y>0)page=d.physicalPageSize;
            Vector2 n=DecorationRenderer.EffectiveSize(def,d);Vector2 p=Vector2.Scale(Vector2.Scale(n,page),corner);
            float c=Mathf.Cos(d.rotationDegrees*Mathf.Deg2Rad),s=Mathf.Sin(d.rotationDegrees*Mathf.Deg2Rad);
            return new Vector2((p.x*c-p.y*s)/page.x,(p.x*s+p.y*c)/page.y);
        }
        public static bool Hit(DecorationDefinition def,JournalDecorationData d,Vector2 uv,Vector2 page)
        {
            if(d.physicalMaterial&&d.physicalPageSize.x>0&&d.physicalPageSize.y>0)page=d.physicalPageSize;
            var p=Vector2.Scale(uv-d.position,page);float c=Mathf.Cos(-d.rotationDegrees*Mathf.Deg2Rad),s=Mathf.Sin(-d.rotationDegrees*Mathf.Deg2Rad);
            var n=Vector2.Scale(DecorationRenderer.EffectiveSize(def,d),page);
            var local=new Vector2((p.x*c-p.y*s)/n.x+.5f,(p.x*s+p.y*c)/n.y+.5f);
            return local.x>=0&&local.y>=0&&local.x<=1&&local.y<=1&&def.HitAlpha(local);
        }
    }
}
