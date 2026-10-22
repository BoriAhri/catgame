using System;
using System.Collections.Generic;
using CatHealingHouse.Economy;
using UnityEngine;

namespace CatHealingHouse.View.Art
{
    /// <summary>
    /// 진짜 그림을 끼워 넣는 곳. 인스펙터에서 칸마다 스프라이트를 끌어다 놓으면 그 그림을 쓰고,
    /// 비어 있으면 코드로 그린 임시 그림(CuteArt)을 쓴다. 그림이 하나씩 도착할 때마다 그것만 바꿔 끼우면 된다.
    ///
    /// 그림 규칙 (아트 디렉션 문서 '스프라이트 규격'):
    ///  - 가구·고양이: 피벗은 바닥 중앙. 크기는 자동으로 맞춰지므로 해상도만 넉넉하면 된다.
    ///  - 아이콘: 피벗은 가운데.
    ///  - 고양이 부위: 각 부위 스프라이트의 피벗을 관절 위치에 둔다 (머리는 목, 귀는 귀 뿌리, 다리는 어깨·엉덩이, 꼬리는 뿌리).
    ///    부위 위치는 아래 Joint 값(전체 그림 기준 0~1 좌표)으로 맞춘다.
    /// </summary>
    [CreateAssetMenu(menuName = "Cat Healing House/Art Library", fileName = "ArtLibrary")]
    public sealed class ArtLibrary : ScriptableObject
    {
        public static ArtLibrary Active;

        [Serializable]
        public sealed class FurnitureArt
        {
            public string furnitureId;
            [Tooltip("기본 그림")] public Sprite sprite;
            [Tooltip("화장실만: 깨끗 / 보통 / 더러움 / 매우 더러움")] public Sprite[] stages = new Sprite[0];
        }

        [Serializable]
        public sealed class ItemArt
        {
            public ItemKind kind;
            public Sprite sprite;
        }

        [Serializable]
        public sealed class CatPartArt
        {
            public CatPart part;
            public Sprite sprite;
            [Tooltip("전체 고양이 그림 기준 관절 위치 (0~1). 비워두면(0,0) 기본값")]
            public Vector2 joint;
            [Tooltip("테두리 실루엣 (있으면 모든 부위 색칠 밑에 깔아서 바깥 둘레에만 선이 보이게)")]
            public Sprite outline;
        }

        /// <summary>자세 한 장짜리 몸 그림 종류 (앉기, 식빵, 엎드리기, 웅크려 자기, 쭉 뻗기, 발라당).</summary>
        public enum CatPoseArt { Sit, Loaf, Prone, Curl, Sprawl, Belly }

        [Serializable]
        public sealed class PoseArt
        {
            public CatPoseArt pose;
            [Tooltip("몸·다리·꼬리를 한 장에 그린 그림 (피벗 = 발바닥 바닥)")] public Sprite sprite;
            public Sprite outline;
            [Tooltip("움직이는 머리를 따로 붙이는 자세면 켬 (그림에서 머리를 지워 둠)")] public bool hasHead;
            [Tooltip("머리 관절 위치 (루트 기준, 캔버스 단위)")] public Vector2 head;
        }

        [Serializable]
        public sealed class CatSkin
        {
            [Tooltip("고양이 스킨 ID (예: cheese, kkamang)")] public string skinId;
            public CatPartArt[] parts = new CatPartArt[0];
            [Tooltip("프로필 사진 (정사각형 전신 그림). 비워두면 코드 그림")] public Sprite portrait;
            [Tooltip("자세별 한 장짜리 몸 그림 (있으면 그 자세에서 부위 대신 씀)")] public PoseArt[] poses = new PoseArt[0];
        }

        public List<FurnitureArt> furniture = new List<FurnitureArt>();
        public List<ItemArt> items = new List<ItemArt>();
        public List<CatSkin> catSkins = new List<CatSkin>();

        public Sprite FurnitureSprite(string id, int variant)
        {
            foreach (var f in furniture)
            {
                if (f == null || f.furnitureId != id) continue;
                if (f.stages != null && variant >= 0 && variant < f.stages.Length && f.stages[variant] != null) return f.stages[variant];
                return f.sprite;
            }
            return null;
        }

        public Sprite ItemSprite(ItemKind kind)
        {
            foreach (var i in items) if (i != null && i.kind == kind) return i.sprite;
            return null;
        }

        public Sprite CatPartSprite(string skinId, CatPart part)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            foreach (var s in catSkins)
            {
                if (s == null || s.skinId != skinId) continue;
                foreach (var p in s.parts) if (p != null && p.part == part) return p.sprite;
            }
            return null;
        }

        /// <summary>부위 테두리 실루엣 (없으면 null).</summary>
        public Sprite CatPartOutline(string skinId, CatPart part)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            foreach (var s in catSkins)
            {
                if (s == null || s.skinId != skinId) continue;
                foreach (var p in s.parts) if (p != null && p.part == part) return p.outline;
            }
            return null;
        }

        /// <summary>자세별 몸 그림 목록 (없으면 빈 배열).</summary>
        public PoseArt[] PoseArts(string skinId)
        {
            if (!string.IsNullOrEmpty(skinId))
                foreach (var s in catSkins) if (s != null && s.skinId == skinId && s.poses != null) return s.poses;
            return new PoseArt[0];
        }

        /// <summary>프로필 사진 (없으면 null).</summary>
        public Sprite PortraitSprite(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            foreach (var s in catSkins) if (s != null && s.skinId == skinId) return s.portrait;
            return null;
        }

        /// <summary>진짜 그림의 관절 위치. 지정이 없으면 코드 그림의 기본 관절.</summary>
        public Vector2 CatJoint(string skinId, CatPart part)
        {
            if (!string.IsNullOrEmpty(skinId))
                foreach (var s in catSkins)
                {
                    if (s == null || s.skinId != skinId) continue;
                    foreach (var p in s.parts)
                        if (p != null && p.part == part && p.sprite != null && p.joint != Vector2.zero) return p.joint;
                }
            return CatParts.Joint[part];
        }
    }
}
