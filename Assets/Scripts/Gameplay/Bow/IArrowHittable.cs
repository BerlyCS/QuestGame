/// <summary>
/// Implemented by any object that can be hit by a fired <see cref="Arrow"/>.
/// Ported from the OOT shooting gallery and kept framework-agnostic.
/// </summary>
public interface IArrowHittable
{
    void Hit(Arrow arrow);
}
