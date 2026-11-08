using UnityEngine;
namespace VintageJournal
{
    /// <summary>One frame of input belongs to one mode; hardware producers set this before legacy adapters run.</summary>
    public static class RuntimeInputGate
    {
        static int consumedFrame=-1;
        public static bool WorkspaceOwnsInput;
        public static bool BlocksLegacyInput;
        public static bool BlocksJournalOperations;
        public static bool ConsumedThisFrame=>consumedFrame==Time.frameCount;
        public static void Consume(){consumedFrame=Time.frameCount;}
        public static void Reset(){consumedFrame=-1;WorkspaceOwnsInput=false;BlocksLegacyInput=false;BlocksJournalOperations=false;}
    }
}
