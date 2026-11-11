using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 虚拟摇杆UI组件
/// 挂在摇杆底座物体上，拖动时带动摇杆头在底座范围内移动
/// 对外提供Direction(方向)与Magnitude(力度)供玩家控制器读取
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    /// <summary>
    /// 摇杆头
    /// </summary>
    public RectTransform Knob;

    /// <summary>
    /// 当前摇杆实例
    /// </summary>
    public static VirtualJoystick Instance { get; private set; }

    /// <summary>
    /// 摇杆方向(已归一化)，无输入时为零向量
    /// </summary>
    public Vector2 Direction { get; private set; }

    /// <summary>
    /// 摇杆力度0~1
    /// </summary>
    public float Magnitude { get; private set; }

    /// <summary>
    /// 是否正在按住摇杆
    /// </summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    /// 摇杆头最大拖动半径
    /// </summary>
    private float m_Radius;

    /// <summary>
    /// 摇杆底座RectTransform
    /// </summary>
    private RectTransform m_BaseRect;

    private void Awake()
    {
        Instance = this;
        m_BaseRect = GetComponent<RectTransform>();
        if (m_BaseRect != null && Knob != null)
        {
            //底座半径减去摇杆头半径，保证摇杆头不超出底座边缘
            m_Radius = (m_BaseRect.sizeDelta.x - Knob.sizeDelta.x) * 0.5f;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 按下摇杆：立即把摇杆头吸附到按下位置
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        IsDragging = true;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (m_BaseRect == null || Knob == null)
        {
            return;
        }
        //屏幕坐标转底座本地坐标
        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_BaseRect, eventData.position, eventData.pressEventCamera, out localPoint))
        {
            return;
        }
        //限制摇杆头在拖动半径内
        Vector2 offset = Vector2.ClampMagnitude(localPoint, m_Radius);
        Knob.anchoredPosition = offset;
        Magnitude = m_Radius > 0 ? Mathf.Clamp01(offset.magnitude / m_Radius) : 0;
        Direction = offset.sqrMagnitude > 0.01f ? offset.normalized : Vector2.zero;
    }

    /// <summary>
    /// 松开摇杆：摇杆头复位
    /// </summary>
    public void OnPointerUp(PointerEventData eventData)
    {
        IsDragging = false;
        Direction = Vector2.zero;
        Magnitude = 0;
        if (Knob != null)
        {
            Knob.anchoredPosition = Vector2.zero;
        }
    }
}
