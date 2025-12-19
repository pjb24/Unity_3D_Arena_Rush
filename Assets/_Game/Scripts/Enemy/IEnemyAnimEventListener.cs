public interface IEnemyAnimEventListener
{
    void OnAttackHitboxOnAll();
    void OnAttackHitboxOffAll();

    void OnAttackHitboxOnIndex(int index);
    void OnAttackHitboxOffIndex(int index);

    void OnAttackEnd();
}
