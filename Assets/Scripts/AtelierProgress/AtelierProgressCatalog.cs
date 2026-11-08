using System;
using System.Globalization;
using System.Linq;

namespace VintageJournal
{
    public static class AtelierProgressCatalog
    {
        public static readonly string[] UpgradeIds={"desk-lamp","memory-shelf","sky-charm","unlock-stamp","unlock-sticker-maker","unlock-photo"};
        public static string SettingName(int setting)=>setting==1?"白天":setting==2?"午后":"夜晚";
        public static string DateId(DateTime now)=>now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        public static string TodayPrompt(DateTime now)=>new[]{"今天是什么颜色？","记下一件让你微笑的小事。","如果今天是一片云，它是什么形状？","你想替哪一刻留一盏灯？","今天听到的声音，最像哪种纸？","写给未来的自己一句温柔的话。","今天有没有一个想要收藏的人？"}[(int)(now.Date.Ticks/TimeSpan.TicksPerDay%7)];
        public static bool IsUpgrade(string id)=>Array.IndexOf(UpgradeIds,id)>=0;
        public static int Price(string id)=>id=="desk-lamp"?10:id=="memory-shelf"?20:id=="sky-charm"?16:id=="unlock-stamp"?8:id=="unlock-sticker-maker"?12:id=="unlock-photo"?16:-1;
        public static string UpgradeName(string id)=>id=="desk-lamp"?"星光小台灯":id=="memory-shelf"?"回忆陈列架":id=="sky-charm"?"云端风铃":id=="unlock-stamp"?"印章与墨水":id=="unlock-sticker-maker"?"贴纸工坊":id=="unlock-photo"?"工作室照片":"";
        public static int Completed(JournalDocument doc)=>doc?.commissionDesk?.letters?.Count(l=>l.status==CommissionLetterStatus.Replied)??0;
        public static int Reputation(JournalDocument doc)=>1+Completed(doc)/3;
        public static bool ToolUnlocked(JournalDocument doc,string tool)
        {
            if(tool=="text")return true;
            if(tool=="sticker")tool="sticker-maker";
            if(tool!="stamp"&&tool!="sticker-maker"&&tool!="photo")return false;
            int needed=tool=="stamp"?1:tool=="sticker-maker"?2:3;
            return Completed(doc)>=needed||(doc?.atelierProgress?.purchases?.Any(p=>p.id=="unlock-"+tool)??false);
        }
        public static string SeasonalAsset(DateTime now)
        {
            var lunar=new ChineseLunisolarCalendar();
            if(now>=lunar.MinSupportedDateTime&&now<=lunar.MaxSupportedDateTime)
            {
                int month=lunar.GetMonth(now),leap=lunar.GetLeapMonth(lunar.GetYear(now));
                if(leap>0&&month>=leap)month--;int day=lunar.GetDayOfMonth(now);
                if(month==7&&day>=4&&day<=10)return "season-firework";
                if(month==8&&day>=10&&day<=20)return "season-mooncake";
                if(month==1&&day<=15)return "season-newyear";
            }
            if(now.Month==12&&now.Day>=25||now.Month==1&&now.Day<=7)return "season-newyear";
            if(now.Month>=3&&now.Month<=5)return "season-sakura";
            if(now.Month>=6&&now.Month<=8)return "season-firework";
            if(now.Month>=9&&now.Month<=10)return "season-mooncake";
            return "season-snowflake";
        }
    }
}
