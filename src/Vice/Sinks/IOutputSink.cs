namespace Vice;

public interface IOutputSink
{
    void Line(string text);
    void Line();
    void Write(string text);
    void Error(string text);
}
