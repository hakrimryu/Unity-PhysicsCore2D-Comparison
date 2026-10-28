using System;
using UnityEngine;
using PhysicsComparisonLab.Battle;

namespace PhysicsComparisonLab.Simulation
{
    /// <summary>
    /// 実在する二つの物理実装の交換境界です。
    /// [KO] 두 실제 물리 구현을 교체하는 경계입니다.
    /// [EN] Replacement boundary between the two actual physics implementations.
    /// </summary>
    public interface IPhysicsSimulation : IDisposable
    {
        /// <summary>
        /// 準備用bodyを分割生成し、完了時にtrueを返します。
        /// [KO] 준비 body를 나눠 생성하고 완료하면 true를 반환합니다.
        /// [EN] Creates preparation bodies in batches and returns true when complete.
        /// </summary>
        bool CreateNextBatch(int maximum);

        /// <summary>
        /// 所有backendが報告するbody数です。境界bodyも含みます。
        /// [KO] 소유 backend가 보고하는 body 수입니다. 경계 body도 포함합니다.
        /// [EN] Body count reported by the owned backend, including boundary bodies.
        /// </summary>
        int BodyCount { get; }

        /// <summary>
        /// 所有backendが報告する衝突shape数です。
        /// [KO] 소유 backend가 보고하는 충돌 shape 수입니다.
        /// [EN] Collision shape count reported by the owned backend.
        /// </summary>
        int ShapeCount { get; }

        /// <summary>
        /// 比較記録用にbackend固有の実行設定を返します。
        /// [KO] 비교 기록을 위해 backend별 실행 설정을 반환합니다.
        /// [EN] Reports backend-specific execution settings for comparison records.
        /// </summary>
        string Configuration { get; }

        /// <summary>
        /// 生存bodyに毎秒の目標速度を設定します。
        /// [KO] 살아 있는 body에 초당 목표 속도를 설정합니다.
        /// [EN] Sets the target velocity in world units per second for a live body.
        /// </summary>
        void SetVelocity(int id, Vector2 velocity);

        /// <summary>
        /// 自動進行せず、指定時間を一度だけ同期実行します。
        /// [KO] 자동 진행 없이 지정 시간을 한 번 동기 실행합니다.
        /// [EN] Advances exactly once synchronously with automatic stepping disabled.
        /// </summary>
        void Step(float seconds);

        /// <summary>
        /// 完了した物理位置を共通状態へ読み戻します。
        /// [KO] 완료된 물리 위치를 공통 상태로 읽어옵니다.
        /// [EN] Reads completed physics positions into shared state.
        /// </summary>
        void CopyPositions(UnitState[] units);

        /// <summary>
        /// native円形queryに固定標的のshapeが含まれるか調べます。
        /// [KO] native 원형 query에 고정 표적의 shape가 포함되는지 확인합니다.
        /// [EN] Checks whether the locked target shape occurs in a native circle query.
        /// </summary>
        bool Overlaps(int attacker, int target, float range);

        /// <summary>
        /// 死亡bodyとshapeを以後の物理処理から除去します。
        /// [KO] 사망 body와 shape를 이후 물리 처리에서 제거합니다.
        /// [EN] Removes a dead body and its shape from subsequent physics work.
        /// </summary>
        void Remove(int id);
    }
}
