using UnityEngine;

// Лабораторная заглушка для проверки reflection-моста, не реализация UHFPS.
namespace UHFPS.Runtime
{
    public class LookController : MonoBehaviour
    {
        public enum ForwardStyle { RootForward, LookForward }

        public ForwardStyle PlayerForward = ForwardStyle.LookForward;
        public Vector2 LookRotation;

        public Transform body;
        public Transform head;

        private void LateUpdate()
        {
            // Воспроизводим обновление трансформов из сохранённых углов.
            if (PlayerForward == ForwardStyle.LookForward)
            {
                if (head != null)
                {
                    head.localRotation = Quaternion.Euler(LookRotation.y, LookRotation.x, 0f);
                }
            }
            else
            {
                if (body != null)
                {
                    body.localRotation = Quaternion.Euler(0f, LookRotation.x, 0f);
                }

                if (head != null)
                {
                    head.localRotation = Quaternion.Euler(LookRotation.y, 0f, 0f);
                }
            }
        }
    }

}
