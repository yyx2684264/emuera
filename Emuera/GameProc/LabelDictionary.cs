﻿using System.Collections.Generic;

namespace MinorShift.Emuera.GameProc
{
    //1.713 LogicalLine.csから分割
    /// <summary>
    ///     ラベルのジャンプ先の辞書。Erbファイル読み込み時に作成
    /// </summary>
    internal sealed class LabelDictionary
    {
        private int currentFileCount;
        private readonly List<FunctionLabelLine> invalidList = new List<FunctionLabelLine>();

        /// <summary>
        ///     本体。全てのFunctionLabelLineを記録
        /// </summary>
        private readonly Dictionary<string, List<FunctionLabelLine>> labelAtDic =
            new Dictionary<string, List<FunctionLabelLine>>();

        private readonly List<GotoLabelLine> labelDollarList = new List<GotoLabelLine>();

        private readonly Dictionary<string, int> loadedFileDic = new Dictionary<string, int>();
        private int totalFileCount;

        public LabelDictionary()
        {
            Initialized = false;
        }

        public int Count { get; private set; }

        /// <summary>
        ///     これがfalseである間は式中関数は呼べない
        ///     （つまり関数宣言の初期値として式中関数は使えない）
        /// </summary>
        public bool Initialized { get; set; }


        public List<FunctionLabelLine>[] GetEventLabels(string key)
        {
            List<FunctionLabelLine>[] ret = null;
            eventLabelDic.TryGetValue(key, out ret);
            return ret;
        }

        public FunctionLabelLine GetNonEventLabel(string key)
        {
            FunctionLabelLine ret = null;
            noneventLabelDic.TryGetValue(key, out ret);
            return ret;
        }

        public List<FunctionLabelLine> GetAllLabels(bool getInvalidList)
        {
            var ret = new List<FunctionLabelLine>();
            foreach (var list in labelAtDic.Values)
                ret.AddRange(list);
            if (getInvalidList)
                ret.AddRange(invalidList);
            return ret;
        }

        public GotoLabelLine GetLabelDollar(string key, FunctionLabelLine labelAtLine)
        {
            foreach (var label in labelDollarList)
                if (label.LabelName == key && label.ParentLabelLine == labelAtLine)
                    return label;
            return null;
        }

        internal void AddInvalidLabel(FunctionLabelLine invalidLabelLine)
        {
            invalidList.Add(invalidLabelLine);
        }

        #region Initialized 前用

        public FunctionLabelLine GetSameNameLabel(FunctionLabelLine point)
        {
            var id = point.LabelName;
            if (!labelAtDic.ContainsKey(id))
                return null;
            if (point.IsError)
                return null;
            var labelList = labelAtDic[id];
            if (labelList.Count <= 1)
                return null;
            return labelList[0];
        }


        private readonly Dictionary<string, List<FunctionLabelLine>[]> eventLabelDic =
            new Dictionary<string, List<FunctionLabelLine>[]>();

        private readonly Dictionary<string, FunctionLabelLine> noneventLabelDic =
            new Dictionary<string, FunctionLabelLine>();

        public void SortLabels()
        {
            foreach (var pair in eventLabelDic)
            foreach (var list in pair.Value)
                list.Clear();
            eventLabelDic.Clear();
            noneventLabelDic.Clear();
            foreach (var pair in labelAtDic)
            {
                var key = pair.Key;
                var list = pair.Value;
                if (list.Count > 1)
                    list.Sort();
                if (!list[0].IsEvent)
                {
                    noneventLabelDic.Add(key, list[0]);
                    GlobalStatic.IdentifierDictionary.resizeLocalVars("ARG", list[0].LabelName, list[0].ArgLength);
                    GlobalStatic.IdentifierDictionary.resizeLocalVars("ARGS", list[0].LabelName, list[0].ArgsLength);
                    continue;
                }
                //1810alpha010 オプションによりイベント関数をイベント関数でないかのように呼び出すことを許可
                //eramaker仕様 - #PRI #LATER #SINGLE等を無視し、最先に定義された関数1つのみを呼び出す
                if (Config.CompatiCallEvent)
                    noneventLabelDic.Add(key, list[0]);
                var eventLabels = new List<FunctionLabelLine>[4];
                var onlylist = new List<FunctionLabelLine>();
                var prilist = new List<FunctionLabelLine>();
                var normallist = new List<FunctionLabelLine>();
                var laterlist = new List<FunctionLabelLine>();
                var localMax = 0;
                var localsMax = 0;
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].LocalLength > localMax)
                        localMax = list[i].LocalLength;
                    if (list[i].LocalsLength > localsMax)
                        localsMax = list[i].LocalsLength;
                    if (list[i].IsOnly)
                        onlylist.Add(list[i]);
                    if (list[i].IsPri)
                        prilist.Add(list[i]);
                    if (list[i].IsLater)
                        laterlist.Add(list[i]); //#PRIかつ#LATERなら二重に登録する。eramakerの仕様
                    if (!list[i].IsPri && !list[i].IsLater)
                        normallist.Add(list[i]);
                }
                if (localMax < GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCAL"))
                    localMax = GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCAL");
                if (localsMax < GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALS"))
                    localsMax = GlobalStatic.IdentifierDictionary.getLocalDefaultSize("LOCALS");
                eventLabels[0] = onlylist;
                eventLabels[1] = prilist;
                eventLabels[2] = normallist;
                eventLabels[3] = laterlist;
                for (var i = 0; i < 4; i++)
                for (var j = 0; j < eventLabels[i].Count; j++)
                {
                    eventLabels[i][j].LocalLength = localMax;
                    eventLabels[i][j].LocalsLength = localsMax;
                }
                eventLabelDic.Add(key, eventLabels);
            }
        }

        public void RemoveAll()
        {
            Initialized = false;
            Count = 0;
            foreach (var pair in eventLabelDic)
            foreach (var list in pair.Value)
                list.Clear();
            eventLabelDic.Clear();
            noneventLabelDic.Clear();

            foreach (var pair in labelAtDic)
                pair.Value.Clear();
            labelAtDic.Clear();
            labelDollarList.Clear();
            loadedFileDic.Clear();
            invalidList.Clear();
            currentFileCount = 0;
            totalFileCount = 0;
        }

        public void RemoveLabelWithPath(string fname)
        {
            List<FunctionLabelLine> labelLines;
            var removeLine = new List<FunctionLabelLine>();
            var removeKey = new List<string>();
            foreach (var pair in labelAtDic)
            {
                var key = pair.Key;
                labelLines = pair.Value;
                foreach (var labelLine in labelLines)
                    if (string.Equals(labelLine.Position.Filename, fname, Config.SCIgnoreCase))
                        removeLine.Add(labelLine);
                foreach (var remove in removeLine)
                {
                    labelLines.Remove(remove);
                    if (labelLines.Count == 0)
                        removeKey.Add(key);
                }
                removeLine.Clear();
            }
            foreach (var rKey in removeKey)
                labelAtDic.Remove(rKey);
            for (var i = 0; i < invalidList.Count; i++)
                if (string.Equals(invalidList[i].Position.Filename, fname, Config.SCIgnoreCase))
                {
                    invalidList.RemoveAt(i);
                    i--;
                }
        }


        public void AddFilename(string filename)
        {
            var curCount = 0;
            if (loadedFileDic.TryGetValue(filename, out curCount))
            {
                currentFileCount = curCount;
                RemoveLabelWithPath(filename);
                return;
            }
            totalFileCount++;
            currentFileCount = totalFileCount;
            loadedFileDic.Add(filename, totalFileCount);
        }

        public void AddLabel(FunctionLabelLine point)
        {
            point.Index = Count;
            point.FileIndex = currentFileCount;
            Count++;
            var id = point.LabelName;
            if (labelAtDic.ContainsKey(id))
            {
                labelAtDic[id].Add(point);
            }
            else
            {
                var labelList = new List<FunctionLabelLine>();
                labelList.Add(point);
                labelAtDic.Add(id, labelList);
            }
        }

        public bool AddLabelDollar(GotoLabelLine point)
        {
            var id = point.LabelName;
            foreach (var label in labelDollarList)
                if (label.LabelName == id && label.ParentLabelLine == point.ParentLabelLine)
                    return false;
            labelDollarList.Add(point);
            return true;
        }

        #endregion
    }
}