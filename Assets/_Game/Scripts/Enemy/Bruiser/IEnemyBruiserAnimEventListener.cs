public interface IEnemyBruiserAnimEventListener
{
    void OnAttackHitboxOnAll();
    void OnAttackHitboxOffAll();

    void OnAttackHitboxOnIndex(int index);
    void OnAttackHitboxOffIndex(int index);

    void OnAttackEnd();
}
