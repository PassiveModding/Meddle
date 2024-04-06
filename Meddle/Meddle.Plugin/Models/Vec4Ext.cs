using System.Numerics;
using SkiaSharp;

namespace Meddle.Plugin.Models;

public struct Vec4Ext
{
    private Vector4 value;
    
    public float X { get => value.X; set => this.value.X = value; }
    public float Y { get => value.Y; set => this.value.Y = value; }
    public float Z { get => value.Z; set => this.value.Z = value; }
    public float W { get => value.W; set => this.value.W = value; }
    public Vector3 XYZ
    {
        get => new(value.X, value.Y, value.Z);
        set
        {
            this.value = new Vector4(value, this.value.W);
        }
    }
    
    public Vector2 XY
    {
        get => new(value.X, value.Y);
        set
        {
            this.value = new Vector4(value, this.value.Z, this.value.W);
        }
    }
    
    public Vector2 ZW
    {
        get => new(value.Z, value.W);
        set
        {
            this.value = new Vector4(this.value.X, this.value.Y, value.X, value.Y);
        }
    }
    
    public Vec4Ext(Vector4 value)
    {
        this.value = value;
    }
    
    public Vec4Ext(float x, float y, float z, float w)
    {
        this.value = new Vector4(x, y, z, w);
    }
    
    public Vec4Ext(Vector3 xyz, float w)
    {
        this.value = new Vector4(xyz, w);
    }
    
    public Vec4Ext(SKColor color)
    {
        this.value = new Vector4(color.Red, color.Green, color.Blue, color.Alpha) / 255f;
    }
    
    public static implicit operator Vector4(Vec4Ext vec) => vec.value;
    public static implicit operator Vec4Ext(Vector4 vec) => new(vec);
    public static implicit operator SKColor(Vec4Ext vec) => new((byte)(vec.value.X * 255), (byte)(vec.value.Y * 255), (byte)(vec.value.Z * 255), (byte)(vec.value.W * 255));
    public static implicit operator Vec4Ext(SKColor color) => new(color);
    public static Vec4Ext operator +(Vec4Ext left, Vec4Ext right) => new(left.value + right.value);
    public static Vec4Ext operator -(Vec4Ext left, Vec4Ext right) => new(left.value - right.value);
    public static Vec4Ext operator *(Vec4Ext left, Vec4Ext right) => new(left.value * right.value);
    public static Vec4Ext operator *(Vec4Ext left, float right) => new(left.value * right);
    public static Vec4Ext operator /(Vec4Ext left, Vec4Ext right) => new(left.value / right.value);
    public static Vec4Ext operator /(Vec4Ext left, float right) => new(left.value / right);
    
    public Vec4Ext WithX(float x)
    {
        this.X = x;
        return this;
    }
    
    public Vec4Ext WithY(float y)
    {
        this.Y = y;
        return this;
    }
    
    public Vec4Ext WithZ(float z)
    {
        this.Z = z;
        return this;
    }
    
    public Vec4Ext WithW(float w)
    {
        this.W = w;
        return this;
    }
}
